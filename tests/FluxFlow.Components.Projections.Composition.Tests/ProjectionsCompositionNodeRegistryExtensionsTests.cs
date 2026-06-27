using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.Projections.Composition;
using FluxFlow.Components.Projections.Contracts;
using FluxFlow.Components.Projections.Diagnostics;
using FluxFlow.Components.Projections.Options;
using FluxFlow.Composition;
using FluxFlow.Composition.Hosting;
using FluxFlow.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Projections.Composition.Tests;

public sealed class ProjectionsCompositionNodeRegistryExtensionsTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public void RegisterEventProjection_registers_request_result_metadata()
    {
        var registry = new CompositionNodeRegistry()
            .RegisterEventProjection();

        var registration = registry.Registrations[ProjectionsCompositionNodeTypes.EventProjection];
        registration.Inputs[ProjectionsCompositionPortNames.Input].MessageType
            .ShouldBe(typeof(ProjectionEvent));
        registration.Outputs[ProjectionsCompositionPortNames.Output].MessageType
            .ShouldBe(typeof(EventProjectionSnapshot));
    }

    [Fact]
    public void Design_metadata_provider_returns_valid_projection_metadata()
    {
        var metadata = ProjectionDesignMetadata();

        metadata.Type.Value.ShouldBe(ProjectionsCompositionNodeTypes.EventProjection);
        metadata.DisplayName.ShouldBe("Event Projection");
        metadata.Category.ShouldBe(new ComponentCategory("Projections"));
        metadata.SuggestedEditorWidth.ShouldBe(460);
        ComponentDesignMetadataValidator.Validate(metadata).ShouldBeEmpty();
        metadata.Options.ShouldNotContain(option =>
            option.Name.Value == ProjectionsCompositionResourceNames.Clock);
        AssertClockResource(metadata);
    }

    [Fact]
    public void Design_metadata_provider_describes_projection_ports()
    {
        var metadata = ProjectionDesignMetadata();

        metadata.Ports.Count.ShouldBe(2);

        var input = metadata.Ports[0];
        input.Name.Value.ShouldBe(ProjectionsCompositionPortNames.Input);
        input.Direction.ShouldBe(PortDirection.Input);
        input.Order.ShouldBe(0);
        input.ValueType.ShouldBe(nameof(ProjectionEvent));
        input.IsPrimary.ShouldBeTrue();

        var output = metadata.Ports[1];
        output.Name.Value.ShouldBe(ProjectionsCompositionPortNames.Output);
        output.Direction.ShouldBe(PortDirection.Output);
        output.Order.ShouldBe(1);
        output.ValueType.ShouldBe(nameof(EventProjectionSnapshot));
        output.IsPrimary.ShouldBeTrue();
    }

    [Fact]
    public void Design_metadata_provider_describes_projection_options()
    {
        var metadata = ProjectionDesignMetadata();
        var defaults = new EventProjectionOptions();

        metadata.Options.Select(option => option.Name.Value).ShouldBe([
            "name",
            "filter",
            "rateWindowSeconds",
            "emitEveryMatch",
            "emitFinalSnapshot",
            "maxPreviewChars",
            "boundedCapacity"
        ], ignoreOrder: false);

        AssertOption(metadata, "name", OptionValueKind.Text, defaultValue: null);

        var filter = metadata.Options.Single(option => option.Name.Value == "filter");
        filter.Kind.ShouldBe(OptionValueKind.Json);
        filter.DefaultValue.ShouldBeOfType<EventFilter>();

        AssertOption(
            metadata,
            "rateWindowSeconds",
            OptionValueKind.Number,
            defaults.RateWindowSeconds,
            min: 0.000001);
        AssertOption(
            metadata,
            "emitEveryMatch",
            OptionValueKind.Boolean,
            defaults.EmitEveryMatch);
        AssertOption(
            metadata,
            "emitFinalSnapshot",
            OptionValueKind.Boolean,
            defaults.EmitFinalSnapshot);
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
        var provider = new ProjectionsComponentDesignMetadataProvider();
        var catalog = ComponentDesignMetadataCatalog.FromProviders([provider]);

        catalog.All.ShouldHaveSingleItem();
        catalog.TryGet(
            new ComponentType(ProjectionsCompositionNodeTypes.EventProjection),
            out var metadata).ShouldBeTrue();
        metadata.ShouldNotBeNull()
            .DisplayName.ShouldBe("Event Projection");
    }

    [Fact]
    public async Task Hosted_event_projection_filters_events_and_preserves_correlation_id()
    {
        var timestamp = DateTimeOffset.Parse("2026-06-18T12:00:00Z");
        var clock = new FakeTimeProvider(timestamp);

        await WithNodeAsync(
            async (input, output, _) =>
            {
                var snapshots = Link(output.Source);
                var first = FlowMessage.Create(
                    CreateEvent(
                        timestamp.AddSeconds(-10),
                        "operation.completed",
                        subject: "orders/1",
                        status: "failed",
                        payloadPreview: "abcdef",
                        attributes: new Dictionary<string, string>
                        {
                            ["tenant"] = "north"
                        }),
                    new CorrelationId("first"));
                var ignored = FlowMessage.Create(CreateEvent(
                    timestamp.AddSeconds(-5),
                    "operation.completed",
                    subject: "orders/2",
                    status: "ok",
                    attributes: new Dictionary<string, string>
                    {
                        ["tenant"] = "north"
                    }));
                var second = FlowMessage.Create(
                    CreateEvent(
                        timestamp.AddSeconds(-1),
                        "operation.completed",
                        subject: "orders/3",
                        status: "failed",
                        payloadPreview: "xyz",
                        attributes: new Dictionary<string, string>
                        {
                            ["tenant"] = "north"
                        }),
                    new CorrelationId("second"));

                (await input.Target.SendAsync(first).WaitAsync(Timeout)).ShouldBeTrue();
                (await input.Target.SendAsync(ignored).WaitAsync(Timeout)).ShouldBeTrue();
                (await input.Target.SendAsync(second).WaitAsync(Timeout)).ShouldBeTrue();

                var firstSnapshot = await snapshots.ReceiveAsync().WaitAsync(Timeout);
                var secondSnapshot = await snapshots.ReceiveAsync().WaitAsync(Timeout);

                firstSnapshot.CorrelationId.ShouldBe(first.CorrelationId);
                firstSnapshot.Payload.Timestamp.ShouldBe(timestamp);
                firstSnapshot.Payload.Name.ShouldBe("errors");
                firstSnapshot.Payload.ObservedCount.ShouldBe(1);
                firstSnapshot.Payload.MatchedCount.ShouldBe(1);
                firstSnapshot.Payload.Latest.ShouldNotBeNull().PayloadPreview.ShouldBe("abcd");

                secondSnapshot.CorrelationId.ShouldBe(second.CorrelationId);
                secondSnapshot.Payload.ObservedCount.ShouldBe(3);
                secondSnapshot.Payload.MatchedCount.ShouldBe(2);
                secondSnapshot.Payload.CurrentRate.ShouldBe(0.2d);
                secondSnapshot.Payload.Latest.ShouldNotBeNull().Subject.ShouldBe("orders/3");
            },
            node => node
                .Configure("name", "errors")
                .Configure("rateWindowSeconds", 10)
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
                .Resource(ProjectionsCompositionResourceNames.Clock, "fixed"),
            services => services.AddKeyedSingleton<TimeProvider>("fixed", clock));
    }

    [Fact]
    public async Task Hosted_event_projection_binds_nested_filter_configuration()
    {
        var timestamp = DateTimeOffset.Parse("2026-06-18T12:30:00Z");
        await WithNodeAsync(
            async (input, output, _) =>
            {
                var snapshots = Link(output.Source);

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

                var snapshot = await snapshots.ReceiveAsync().WaitAsync(Timeout);
                snapshot.Payload.MatchedCount.ShouldBe(1);
                snapshot.Payload.Filter.TypePrefix.ShouldBe("task.");
                snapshot.Payload.Filter.Status.ShouldBe("failed");
                snapshot.Payload.Filter.SubjectPrefix.ShouldBe("jobs/");
                snapshot.Payload.Filter.Attributes["tenant"].ShouldBe("north");
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
    public async Task Hosted_event_projection_exposes_events()
    {
        await WithNodeAsync(async (input, output, descriptor) =>
        {
            output.Source.LinkTo(DataflowBlock.NullTarget<FlowMessage<EventProjectionSnapshot>>());
            var events = Link(descriptor.Events.ShouldNotBeNull());
            var message = FlowMessage.Create(CreateEvent(
                DateTimeOffset.Parse("2026-06-18T13:00:00Z"),
                "event.created"));

            (await input.Target.SendAsync(message).WaitAsync(Timeout)).ShouldBeTrue();

            var @event = await events.ReceiveAsync().WaitAsync(Timeout);
            @event.Name.ShouldBe(ProjectionDiagnosticNames.ProjectionUpdated);
            @event.CorrelationId.ShouldBe(message.CorrelationId);
            @event.Attributes["matchedCount"].ShouldBe(1L);
        });
    }

    [Fact]
    public async Task Invalid_configuration_surfaces_factory_diagnostic()
    {
        var services = new ServiceCollection();
        services
            .AddFluxFlowComposition(CompositionDefinitionBuilder
                .Create()
                .Workflow("main", workflow => workflow.Node(
                    "projection",
                    ProjectionsCompositionNodeTypes.EventProjection,
                    node => node.Configure("rateWindowSeconds", 0)))
                .Build())
            .RegisterNodes(registry => registry.RegisterEventProjection())
            .Configure(options => options.ThrowOnBuildFailure = false);

        await using var provider = services.BuildServiceProvider();
        await BuildCompositionAsync(provider);

        var host = provider.GetRequiredService<ICompositionRuntimeHost>();
        host.Runtime.ShouldBeNull();
        host.Diagnostics.ShouldContain(diagnostic =>
            diagnostic.Code == CompositionDiagnosticCode.FactoryFailed &&
            diagnostic.Message.Contains(
                "rateWindowSeconds",
                StringComparison.OrdinalIgnoreCase));
    }

    private static ComponentDesignMetadata ProjectionDesignMetadata()
        => new ProjectionsComponentDesignMetadataProvider()
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

        resource.Name.Value.ShouldBe(ProjectionsCompositionResourceNames.Clock);
        resource.DisplayName.ShouldBe("Clock");
        resource.Order.ShouldBe(0);
        resource.IsRequired.ShouldBeFalse();
        resource.ValueType.ShouldBe(nameof(TimeProvider));
    }

    private static async Task WithNodeAsync(
        Func<
            CompositionInputPort<ProjectionEvent>,
            CompositionOutputPort<EventProjectionSnapshot>,
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
                    ProjectionsCompositionNodeTypes.EventProjection,
                    configureNode))
                .Build())
            .RegisterNodes(registry => registry.RegisterEventProjection())
            .Configure(options => options.StartRuntimeWithHost = false);

        await using var provider = services.BuildServiceProvider();
        await BuildCompositionAsync(provider);

        var descriptor = provider.GetRequiredService<ICompositionRuntimeHost>()
            .Runtime.ShouldNotBeNull()
            .Nodes.ShouldHaveSingleItem()
            .Descriptor;
        var input = descriptor.Inputs[ProjectionsCompositionPortNames.Input]
            .ShouldBeOfType<CompositionInputPort<ProjectionEvent>>();
        var output = descriptor.Outputs[ProjectionsCompositionPortNames.Output]
            .ShouldBeOfType<CompositionOutputPort<EventProjectionSnapshot>>();

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
}
