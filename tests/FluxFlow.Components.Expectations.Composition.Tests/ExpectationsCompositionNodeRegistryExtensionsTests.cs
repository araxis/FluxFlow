using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.Expectations;
using FluxFlow.Components.Expectations.Composition;
using FluxFlow.Components.Expectations.Contracts;
using FluxFlow.Components.Expectations.Diagnostics;
using FluxFlow.Components.Expectations.Nodes;
using FluxFlow.Components.Expectations.Options;
using FluxFlow.Components.Projections.Contracts;
using FluxFlow.Composition;
using FluxFlow.Composition.Hosting;
using FluxFlow.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Expectations.Composition.Tests;

public sealed class ExpectationsCompositionNodeRegistryExtensionsTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public void RegisterEventExpectation_registers_request_result_metadata()
    {
        var registry = new CompositionNodeRegistry()
            .RegisterEventExpectation();

        var registration = registry.Registrations[ExpectationsCompositionNodeTypes.EventExpectation];
        registration.Inputs[ExpectationsCompositionPortNames.Input].MessageType
            .ShouldBe(typeof(ProjectionEvent));
        registration.Outputs[ExpectationsCompositionPortNames.Output].MessageType
            .ShouldBe(typeof(EventExpectationResult));
    }

    [Fact]
    public void Design_metadata_provider_returns_valid_expectation_metadata()
    {
        var metadata = ExpectationDesignMetadata();

        metadata.Type.Value.ShouldBe(ExpectationsCompositionNodeTypes.EventExpectation);
        metadata.DisplayName?.Value.ShouldBe("Event Expectation");
        metadata.Category.ShouldBe(new ComponentCategory("Expectations"));
        metadata.SuggestedEditorWidth.ShouldBe(460);
        ComponentDesignMetadataValidator.Validate(metadata).ShouldBeEmpty();
        metadata.Options.ShouldNotContain(option =>
            option.Name.Value == ExpectationsCompositionResourceNames.Clock);
        AssertClockResource(metadata);
    }

    [Fact]
    public void Design_metadata_provider_describes_expectation_ports()
    {
        var metadata = ExpectationDesignMetadata();

        metadata.Ports.Count.ShouldBe(2);

        var input = metadata.Ports[0];
        input.Name.Value.ShouldBe(ExpectationsCompositionPortNames.Input);
        input.Direction.ShouldBe(PortDirection.Input);
        input.Order.ShouldBe(0);
        input.ValueType?.Value.ShouldBe(nameof(ProjectionEvent));
        input.IsPrimary.ShouldBeTrue();

        var output = metadata.Ports[1];
        output.Name.Value.ShouldBe(ExpectationsCompositionPortNames.Output);
        output.Direction.ShouldBe(PortDirection.Output);
        output.Order.ShouldBe(1);
        output.ValueType?.Value.ShouldBe(nameof(EventExpectationResult));
        output.IsPrimary.ShouldBeTrue();
    }

    [Fact]
    public void Design_metadata_provider_describes_expectation_options()
    {
        var metadata = ExpectationDesignMetadata();
        var defaults = new EventExpectationOptions();

        metadata.Options.Select(option => option.Name.Value).ShouldBe([
            "kind",
            "name",
            "filter",
            "timeoutMilliseconds",
            "maxObservedEvents",
            "maxPreviewChars",
            "boundedCapacity"
        ], ignoreOrder: false);

        var kind = metadata.Options.Single(option => option.Name.Value == "kind");
        kind.Kind.ShouldBe(OptionValueKind.Enum);
        kind.DefaultValue.ShouldBe(defaults.Kind.ToString());
        kind.Choices.Select(choice => choice.Value.Value).ShouldBe([
            EventExpectationNodeKind.Expect.ToString(),
            EventExpectationNodeKind.Guard.ToString()
        ], ignoreOrder: false);

        AssertOption(metadata, "name", OptionValueKind.Text, defaultValue: null);

        var filter = metadata.Options.Single(option => option.Name.Value == "filter");
        filter.Kind.ShouldBe(OptionValueKind.Json);
        filter.DefaultValue.ShouldBeOfType<EventFilter>();

        AssertOption(
            metadata,
            "timeoutMilliseconds",
            OptionValueKind.Number,
            defaultValue: null,
            min: 0.000001);
        AssertOption(
            metadata,
            "maxObservedEvents",
            OptionValueKind.Number,
            defaults.MaxObservedEvents,
            min: 0);
        AssertOption(
            metadata,
            "maxPreviewChars",
            OptionValueKind.Number,
            defaults.MaxPreviewChars,
            min: 0);
        AssertOption(
            metadata,
            "boundedCapacity",
            OptionValueKind.Number,
            defaults.BoundedCapacity,
            min: 1);
    }

    [Fact]
    public void Design_metadata_provider_loads_into_catalog()
    {
        var provider = new ExpectationsComponentDesignMetadataProvider();
        var catalog = ComponentDesignMetadataCatalog.FromProviders([provider]);

        catalog.All.ShouldHaveSingleItem();
        catalog.TryGet(
            new ComponentType(ExpectationsCompositionNodeTypes.EventExpectation),
            out var metadata).ShouldBeTrue();
        metadata.ShouldNotBeNull()
            .DisplayName?.Value.ShouldBe("Event Expectation");
    }

    [Fact]
    public async Task Hosted_expectation_matches_events_and_preserves_correlation_id()
    {
        var timestamp = DateTimeOffset.Parse("2026-06-18T12:00:00Z");
        var clock = new FakeTimeProvider(timestamp);

        await WithNodeAsync(
            async (input, output, _) =>
            {
                var results = Link(output.Source);
                var ignored = FlowMessage.Create(CreateEvent(
                    timestamp.AddSeconds(-1),
                    "operation.completed",
                    subject: "orders/1",
                    status: "failed",
                    attributes: new Dictionary<string, string>
                    {
                        ["tenant"] = "south"
                    }));
                var matched = FlowMessage.Create(
                    CreateEvent(
                        timestamp,
                        "operation.completed",
                        subject: "orders/2",
                        status: "failed",
                        payloadPreview: "abcdef",
                        attributes: new Dictionary<string, string>
                        {
                            ["tenant"] = "north"
                        }),
                    new CorrelationId("matched"));

                (await input.Target.SendAsync(ignored).WaitAsync(Timeout)).ShouldBeTrue();
                (await input.Target.SendAsync(matched).WaitAsync(Timeout)).ShouldBeTrue();

                var result = await results.ReceiveAsync().WaitAsync(Timeout);
                result.CorrelationId.ShouldBe(matched.CorrelationId);
                result.Payload.EvaluatedAt.ShouldBe(timestamp);
                result.Payload.Name.ShouldBe("failed-order");
                result.Payload.Kind.ShouldBe(EventExpectationResultKind.Expect);
                result.Payload.Satisfied.ShouldBeTrue();
                result.Payload.Matched.ShouldBeTrue();
                result.Payload.TimedOut.ShouldBeFalse();
                result.Payload.MatchedEvent.ShouldNotBeNull().Subject.ShouldBe("orders/2");
                result.Payload.MatchedEvent.PayloadPreview.ShouldBe("abcd");
                result.Payload.ObservedEvents.Count.ShouldBe(2);
            },
            node => node
                .Configure("name", "failed-order")
                .Configure("maxObservedEvents", 2)
                .Configure("maxPreviewChars", 4)
                .Configure(
                    "filter",
                    new EventFilter
                    {
                        Type = "operation.completed",
                        SubjectPrefix = "orders/",
                        Status = "failed",
                        Attributes = new Dictionary<string, string>
                        {
                            ["tenant"] = "north"
                        }
                    })
                .Resource(ExpectationsCompositionResourceNames.Clock, "fixed"),
            services => services.AddKeyedSingleton<TimeProvider>("fixed", clock));
    }

    [Fact]
    public async Task Hosted_expectation_binds_nested_filter_configuration()
    {
        var timestamp = DateTimeOffset.Parse("2026-06-18T12:30:00Z");

        await WithNodeAsync(
            async (input, output, _) =>
            {
                var results = Link(output.Source);

                (await input.Target.SendAsync(FlowMessage.Create(CreateEvent(
                        timestamp,
                        "task.completed",
                        source: "worker",
                        subject: "jobs/42",
                        status: "failed",
                        attributes: new Dictionary<string, string>
                        {
                            ["tenant"] = "north"
                        })))
                    .WaitAsync(Timeout)).ShouldBeTrue();

                var result = await results.ReceiveAsync().WaitAsync(Timeout);
                result.Payload.Filter.TypePrefix.ShouldBe("task.");
                result.Payload.Filter.Status.ShouldBe("failed");
                result.Payload.Filter.SubjectPrefix.ShouldBe("jobs/");
                result.Payload.Filter.Attributes["tenant"].ShouldBe("north");
                result.Payload.Satisfied.ShouldBeTrue();
            },
            node => node.Configure(
                "filter",
                new EventFilter
                {
                    TypePrefix = "task.",
                    SubjectPrefix = "jobs/",
                    Status = "failed",
                    Attributes = new Dictionary<string, string>
                    {
                        ["tenant"] = "north"
                    }
                }));
    }

    [Fact]
    public async Task Hosted_guard_uses_keyed_clock_for_timeout_result()
    {
        var timestamp = DateTimeOffset.Parse("2026-06-18T13:00:00Z");
        var clock = new FakeTimeProvider(timestamp);

        await WithNodeAsync(
            async (_, output, _) =>
            {
                var results = Link(output.Source);

                clock.Advance(TimeSpan.FromMilliseconds(500));

                var result = await results.ReceiveAsync().WaitAsync(Timeout);
                result.Payload.Kind.ShouldBe(EventExpectationResultKind.Guard);
                result.Payload.Satisfied.ShouldBeTrue();
                result.Payload.Matched.ShouldBeFalse();
                result.Payload.TimedOut.ShouldBeTrue();
                result.Payload.EvaluatedAt.ShouldBe(clock.GetUtcNow());
            },
            node => node
                .Configure("kind", EventExpectationNodeKind.Guard)
                .Configure("timeoutMilliseconds", 500)
                .Configure("filter", new EventFilter { Status = "failed" })
                .Resource(ExpectationsCompositionResourceNames.Clock, "fixed"),
            services => services.AddKeyedSingleton<TimeProvider>("fixed", clock));
    }

    [Fact]
    public async Task Hosted_expectation_exposes_events()
    {
        await WithNodeAsync(async (input, output, descriptor) =>
        {
            output.Source.LinkTo(DataflowBlock.NullTarget<FlowMessage<EventExpectationResult>>());
            var events = Link(descriptor.Events.ShouldNotBeNull());
            var message = FlowMessage.Create(CreateEvent(
                DateTimeOffset.Parse("2026-06-18T13:30:00Z"),
                "job.finished"));

            (await input.Target.SendAsync(message).WaitAsync(Timeout)).ShouldBeTrue();

            var @event = await events.ReceiveAsync().WaitAsync(Timeout);
            @event.Name.ShouldBe(ExpectationDiagnosticNames.Matched);
            @event.CorrelationId.ShouldBe(message.CorrelationId);
            @event.Attributes["satisfied"].ShouldBe(true);
        },
        node => node.Configure("filter", new EventFilter { Type = "job.finished" }));
    }

    [Fact]
    public async Task Hosted_expectation_emits_errors_and_continues_after_evaluation_failure()
    {
        await WithNodeAsync(async (input, output, descriptor) =>
        {
            var results = Link(output.Source);
            var errors = Link(descriptor.Errors.ShouldNotBeNull());
            var bad = FlowMessage.Create(
                new ProjectionEvent
                {
                    Timestamp = DateTimeOffset.Parse("2026-06-18T14:00:00Z"),
                    Type = "job.finished",
                    Source = "processor",
                    Attributes = new ThrowingDictionary()
                },
                new CorrelationId("bad"));
            var good = FlowMessage.Create(
                CreateEvent(
                    DateTimeOffset.Parse("2026-06-18T14:00:01Z"),
                    "job.finished",
                    attributes: new Dictionary<string, string>
                    {
                        ["k"] = "v"
                    }),
                new CorrelationId("good"));

            (await input.Target.SendAsync(bad).WaitAsync(Timeout)).ShouldBeTrue();
            (await input.Target.SendAsync(good).WaitAsync(Timeout)).ShouldBeTrue();

            var error = await errors.ReceiveAsync().WaitAsync(Timeout);
            var result = await results.ReceiveAsync().WaitAsync(Timeout);

            error.Code.ShouldBe(ExpectationsErrorCodes.EvaluationFailed);
            error.CorrelationId.ShouldBe(bad.CorrelationId);
            result.CorrelationId.ShouldBe(good.CorrelationId);
            result.Payload.Satisfied.ShouldBeTrue();
        },
        node => node.Configure(
            "filter",
            new EventFilter
            {
                Type = "job.finished",
                Attributes = new Dictionary<string, string>
                {
                    ["k"] = "v"
                }
            }));
    }

    [Theory]
    [InlineData("timeoutMilliseconds", 0)]
    [InlineData("maxObservedEvents", -1)]
    [InlineData("boundedCapacity", 0)]
    public async Task Invalid_configuration_surfaces_factory_diagnostic(
        string optionName,
        object value)
    {
        var services = new ServiceCollection();
        services
            .AddFluxFlowComposition(CompositionDefinitionBuilder
                .Create()
                .Workflow("main", workflow => workflow.Node(
                    "expectation",
                    ExpectationsCompositionNodeTypes.EventExpectation,
                    node => node.Configure(optionName, value)))
                .Build())
            .RegisterNodes(registry => registry.RegisterEventExpectation())
            .Configure(options => options.ThrowOnBuildFailure = false);

        await using var provider = services.BuildServiceProvider();
        await BuildCompositionAsync(provider);

        var host = provider.GetRequiredService<ICompositionRuntimeHost>();
        host.Runtime.ShouldBeNull();
        host.Diagnostics.ShouldContain(diagnostic =>
            diagnostic.Code == CompositionDiagnosticCode.FactoryFailed &&
            (diagnostic.Message.Contains(optionName, StringComparison.OrdinalIgnoreCase) ||
             diagnostic.Message.Contains("capacity", StringComparison.OrdinalIgnoreCase)));
    }

    private static ComponentDesignMetadata ExpectationDesignMetadata()
        => new ExpectationsComponentDesignMetadataProvider()
            .GetMetadata()
            .ShouldHaveSingleItem();

    private static void AssertOption(
        ComponentDesignMetadata metadata,
        string name,
        OptionValueKind kind,
        object? defaultValue,
        double? min = null)
    {
        var option = metadata.Options.Single(option => option.Name.Value == name);
        option.Kind.ShouldBe(kind);
        option.DefaultValue.ShouldBe(defaultValue);
        option.Min.ShouldBe(min);
    }

    private static void AssertClockResource(ComponentDesignMetadata metadata)
    {
        var resource = metadata.Resources.ShouldHaveSingleItem();

        resource.Name.Value.ShouldBe(ExpectationsCompositionResourceNames.Clock);
        resource.DisplayName?.Value.ShouldBe("Clock");
        resource.Order.ShouldBe(0);
        resource.IsRequired.ShouldBeFalse();
        resource.ValueType?.Value.ShouldBe(nameof(TimeProvider));
    }

    private static async Task WithNodeAsync(
        Func<
            CompositionInputPort<ProjectionEvent>,
            CompositionOutputPort<EventExpectationResult>,
            ComposedNode,
            Task> run,
        Action<NodeDefinitionBuilder>? configureNode = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var services = new ServiceCollection();
        configureServices?.Invoke(services);
        services
            .AddFluxFlowComposition(CompositionDefinitionBuilder
                .Create()
                .Workflow("main", workflow => workflow.Node(
                    "node",
                    ExpectationsCompositionNodeTypes.EventExpectation,
                    configureNode))
                .Build())
            .RegisterNodes(registry => registry.RegisterEventExpectation())
            .Configure(options => options.StartRuntimeWithHost = false);

        await using var provider = services.BuildServiceProvider();
        await BuildCompositionAsync(provider);

        var descriptor = provider.GetRequiredService<ICompositionRuntimeHost>()
            .Runtime.ShouldNotBeNull()
            .Nodes.ShouldHaveSingleItem()
            .Descriptor;
        var input = descriptor.Inputs[ExpectationsCompositionPortNames.Input]
            .ShouldBeOfType<CompositionInputPort<ProjectionEvent>>();
        var output = descriptor.Outputs[ExpectationsCompositionPortNames.Output]
            .ShouldBeOfType<CompositionOutputPort<EventExpectationResult>>();

        await run(input, output, descriptor);
    }

    private static async Task BuildCompositionAsync(IServiceProvider provider)
    {
        var hostedService = provider.GetServices<IHostedService>().ShouldHaveSingleItem();
        await hostedService.StartAsync(CancellationToken.None);
    }

    private static BufferBlock<T> Link<T>(ISourceBlock<T> source)
    {
        var buffer = new BufferBlock<T>();
        source.LinkTo(buffer, new DataflowLinkOptions { PropagateCompletion = true });
        return buffer;
    }

    private static ProjectionEvent CreateEvent(
        DateTimeOffset timestamp,
        string type,
        string source = "processor",
        string? subject = null,
        string? status = null,
        string? channel = null,
        string? payloadPreview = null,
        string? sourceNodeId = null,
        IReadOnlyDictionary<string, string>? attributes = null)
        => new()
        {
            Timestamp = timestamp,
            Type = type,
            Source = source,
            SourceNodeId = sourceNodeId,
            Subject = subject,
            Status = status,
            Channel = channel,
            PayloadBytes = payloadPreview?.Length,
            PayloadPreview = payloadPreview,
            Attributes = attributes ?? new Dictionary<string, string>(StringComparer.Ordinal)
        };

    private sealed class ThrowingDictionary : IReadOnlyDictionary<string, string>
    {
        public string this[string key] => throw new InvalidOperationException("boom");
        public IEnumerable<string> Keys => throw new InvalidOperationException("boom");
        public IEnumerable<string> Values => throw new InvalidOperationException("boom");
        public int Count => throw new InvalidOperationException("boom");
        public bool ContainsKey(string key) => throw new InvalidOperationException("boom");
        public bool TryGetValue(string key, out string value) => throw new InvalidOperationException("boom");
        public IEnumerator<KeyValuePair<string, string>> GetEnumerator()
            => throw new InvalidOperationException("boom");
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
            => throw new InvalidOperationException("boom");
    }
}
