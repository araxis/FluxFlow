using System.Collections;
using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.Expectations.Contracts;
using FluxFlow.Components.Expectations.Diagnostics;
using FluxFlow.Components.Expectations.Nodes;
using FluxFlow.Components.Expectations.Options;
using FluxFlow.Components.Projections.Contracts;
using FluxFlow.Composition;
using FluxFlow.Composition.Addressing;
using FluxFlow.Composition.Hosting.DependencyInjection;
using FluxFlow.Composition.Hosting.Revisions;
using FluxFlow.Data;
using FluxFlow.Engine.Hosting;
using FluxFlow.Engine.Ports;
using FluxFlow.Nodes;
using FluxFlow.Testing;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;
using static FluxFlow.Testing.CanonicalTestApplication;

namespace FluxFlow.Components.Expectations.Composition.Tests;

public sealed class ExpectationsCompositionNodeRegistryExtensionsTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private static readonly ApplicationAddress Input = ApplicationAddress.WorkflowPort(
        "main",
        "node",
        ExpectationsCompositionPortNames.Input);
    private static readonly ApplicationAddress Output = ApplicationAddress.WorkflowPort(
        "main",
        "node",
        ExpectationsCompositionPortNames.Output);
    private static readonly ApplicationAddress Events = ApplicationAddress.WorkflowPort(
        "main",
        "node",
        CompositionComponentEvents.PortName);

    [Fact]
    public void RegisterEventExpectation_registers_only_the_canonical_contract()
    {
        var registry = new CompositionNodeRegistry().RegisterEventExpectation();

        var registration = registry.Registrations[
            ExpectationsCompositionNodeTypes.EventExpectation];
        registration.Inputs.Keys.ShouldBe([ExpectationsCompositionPortNames.Input]);
        registration.Outputs.Keys.ShouldBe([
            ExpectationsCompositionPortNames.Output,
            CompositionComponentEvents.PortName
        ], ignoreOrder: false);
        registration.Inputs[ExpectationsCompositionPortNames.Input].MessageType
            .ShouldBe(typeof(ProjectionEvent));
        registration.Outputs[ExpectationsCompositionPortNames.Output].MessageType
            .ShouldBe(typeof(FlowResult<EventExpectationResult>));
    }

    [Fact]
    public void RegisterEventExpectation_supports_explicit_canonical_component_types()
    {
        var registry = new CompositionNodeRegistry()
            .RegisterEventExpectation("event.expect.primary")
            .RegisterEventExpectation("event.expect.secondary");

        registry.Registrations.Keys.ShouldBe([
            "event.expect.primary",
            "event.expect.secondary"
        ], ignoreOrder: false);
        registry.Registrations.Values.ShouldAllBe(registration =>
            registration.Inputs[ExpectationsCompositionPortNames.Input].MessageType ==
                typeof(ProjectionEvent) &&
            registration.Outputs[ExpectationsCompositionPortNames.Output].MessageType ==
                typeof(FlowResult<EventExpectationResult>));
    }

    [Fact]
    public void Design_metadata_provider_returns_valid_expectation_metadata()
    {
        var metadata = DesignMetadata();

        metadata.Type.ShouldBe(new ComponentType(
            ExpectationsCompositionNodeTypes.EventExpectation));
        metadata.DisplayName?.Value.ShouldBe("Event Expectation");
        metadata.Category.ShouldBe(new ComponentCategory("Expectations"));
        metadata.PreferredNodeName.ShouldBe(new ComponentPreferredNodeName("expectEvent"));
        metadata.SuggestedEditorWidth.ShouldBe(460);
        metadata.Options.ShouldNotContain(option =>
            option.Name.Value == ExpectationsCompositionResourceNames.Clock);
        AttributeValue(metadata.Attributes, ComponentDesignMetadataAttributeNames.Aliases)
            .ShouldBe(ExpectationsCompositionNodeTypes.LegacyEventExpectation);
        AssertClockResource(metadata);
        ComponentDesignMetadataValidator.Validate(metadata).ShouldBeEmpty();
    }

    [Fact]
    public void Design_metadata_provider_describes_expectation_ports()
    {
        var metadata = DesignMetadata();

        metadata.Ports.Select(port => (
            port.Name.Value,
            port.Direction,
            port.Order,
            port.ValueType?.Value,
            port.IsPrimary)).ShouldBe([
                (ExpectationsCompositionPortNames.Input, PortDirection.Input, 0,
                    nameof(ProjectionEvent), true),
                (ExpectationsCompositionPortNames.Output, PortDirection.Output, 1,
                    "FlowResult<EventExpectationResult>", true)
            ], ignoreOrder: false);
    }

    [Fact]
    public void Design_metadata_provider_describes_expectation_options()
    {
        var metadata = DesignMetadata();
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

        AssertOption(metadata, "name", OptionValueKind.Text);
        var filter = metadata.Options.Single(option => option.Name.Value == "filter");
        filter.Kind.ShouldBe(OptionValueKind.Json);
        filter.DefaultValue.ShouldBeOfType<EventFilter>();
        AssertOption(metadata, "timeoutMilliseconds", OptionValueKind.Number, min: 0.000001);
        AssertOption(metadata, "maxObservedEvents", OptionValueKind.Number,
            defaults.MaxObservedEvents, min: 0);
        AssertOption(metadata, "maxPreviewChars", OptionValueKind.Number,
            defaults.MaxPreviewChars, min: 0);
        AssertOption(metadata, "boundedCapacity", OptionValueKind.Number,
            defaults.BoundedCapacity, min: 1);
    }

    [Fact]
    public void Design_metadata_provider_describes_expectation_option_hints()
    {
        var options = DesignMetadata().Options.ToDictionary(
            option => option.Name.Value,
            StringComparer.Ordinal);

        AssertOptionHints(options["kind"], "Expectation",
            OptionDesignMetadataAttributeValues.Primary);
        AssertOptionHints(options["name"], "Diagnostics",
            OptionDesignMetadataAttributeValues.Advanced,
            OptionDesignMetadataAttributeValues.Text);
        AssertOptionHints(options["filter"], "Filtering",
            OptionDesignMetadataAttributeValues.Primary,
            OptionDesignMetadataAttributeValues.Json);
        AssertOptionHints(options["timeoutMilliseconds"], "Runtime",
            OptionDesignMetadataAttributeValues.Advanced,
            OptionDesignMetadataAttributeValues.Number);
        AssertOptionHints(options["maxObservedEvents"], "Results",
            OptionDesignMetadataAttributeValues.Advanced,
            OptionDesignMetadataAttributeValues.Number);
        AssertOptionHints(options["maxPreviewChars"], "Preview",
            OptionDesignMetadataAttributeValues.Advanced,
            OptionDesignMetadataAttributeValues.Number);
        AssertOptionHints(options["boundedCapacity"], "Runtime",
            OptionDesignMetadataAttributeValues.Advanced,
            OptionDesignMetadataAttributeValues.Number);
    }

    [Fact]
    public void Design_metadata_provider_uses_canonical_resource_picker_address()
    {
        var resource = DesignMetadata().Resources.ShouldHaveSingleItem();

        AssertResourceHints(
            resource,
            ResourceDesignMetadataAttributeValues.Clock,
            "Resources.{name}");
    }

    [Fact]
    public void Design_metadata_provider_loads_into_catalog()
    {
        var catalog = ComponentDesignMetadataCatalog.FromProviders(
            [new ExpectationsComponentDesignMetadataProvider()]);

        catalog.All.ShouldHaveSingleItem();
        catalog.TryGet(
            new ComponentType(ExpectationsCompositionNodeTypes.EventExpectation),
            out var metadata).ShouldBeTrue();
        metadata.ShouldNotBeNull().DisplayName?.Value.ShouldBe("Event Expectation");
    }

    [Fact]
    public async Task Canonical_host_matches_events_preserves_lineage_and_uses_clock()
    {
        var timestamp = DateTimeOffset.Parse("2026-06-18T12:00:00Z");
        var clock = new FakeTimeProvider(timestamp);

        await WithNodeAsync(
            async (ports, _) =>
            {
                var resultReceive = ports.ReceiveAsync<FlowResult<EventExpectationResult>>(
                    Output,
                    Timeout);
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
                    new CorrelationId("matched"),
                    new TraceId("trace-matched"));

                (await ports.SendAsync(Input, ignored)).IsAccepted.ShouldBeTrue();
                (await ports.SendAsync(Input, matched)).IsAccepted.ShouldBeTrue();

                var result = (await resultReceive).Message.ShouldNotBeNull();
                result.Payload.Kind.ShouldBe(ExpectationResultKinds.Matched);
                result.Payload.IsError.ShouldBeFalse();
                result.CorrelationId.ShouldBe(matched.CorrelationId);
                result.TraceId.ShouldBe(matched.TraceId);
                result.CausationId.ShouldBe(matched.MessageId);
                var value = result.Payload.Value.ShouldNotBeNull();
                value.EvaluatedAt.ShouldBe(timestamp);
                value.Name.ShouldBe("failed-order");
                value.Kind.ShouldBe(EventExpectationResultKind.Expect);
                value.Satisfied.ShouldBeTrue();
                value.Matched.ShouldBeTrue();
                value.TimedOut.ShouldBeFalse();
                value.MatchedEvent.ShouldNotBeNull().Subject.ShouldBe("orders/2");
                value.MatchedEvent.PayloadPreview.ShouldBe("abcd");
                value.ObservedEvents.Count.ShouldBe(2);
            },
            Properties(
                ("name", "failed-order"),
                ("maxObservedEvents", 2),
                ("maxPreviewChars", 4),
                ("filter", new EventFilter
                {
                    Type = "operation.completed",
                    SubjectPrefix = "orders/",
                    Status = "failed",
                    Attributes = new Dictionary<string, string>
                    {
                        ["tenant"] = "north"
                    }
                })),
            clock);
    }

    [Fact]
    public async Task Canonical_host_binds_nested_filter_configuration()
    {
        var timestamp = DateTimeOffset.Parse("2026-06-18T12:30:00Z");

        await WithNodeAsync(
            async (ports, _) =>
            {
                var resultReceive = ports.ReceiveAsync<FlowResult<EventExpectationResult>>(
                    Output,
                    Timeout);
                var message = FlowMessage.Create(CreateEvent(
                    timestamp,
                    "task.completed",
                    source: "worker",
                    subject: "jobs/42",
                    status: "failed",
                    attributes: new Dictionary<string, string>
                    {
                        ["tenant"] = "north"
                    }));

                (await ports.SendAsync(Input, message)).IsAccepted.ShouldBeTrue();
                var value = (await resultReceive).Message.ShouldNotBeNull()
                    .Payload.Value.ShouldNotBeNull();
                value.Filter.TypePrefix.ShouldBe("task.");
                value.Filter.Status.ShouldBe("failed");
                value.Filter.SubjectPrefix.ShouldBe("jobs/");
                value.Filter.Attributes["tenant"].ShouldBe("north");
                value.Satisfied.ShouldBeTrue();
            },
            Properties(("filter", new EventFilter
            {
                TypePrefix = "task.",
                SubjectPrefix = "jobs/",
                Status = "failed",
                Attributes = new Dictionary<string, string>
                {
                    ["tenant"] = "north"
                }
            })));
    }

    [Fact]
    public async Task Canonical_host_uses_keyed_clock_for_timeout_result()
    {
        var timestamp = DateTimeOffset.Parse("2026-06-18T13:00:00Z");
        var clock = new FakeTimeProvider(timestamp);

        await WithNodeAsync(
            async (ports, _) =>
            {
                var resultReceive = ports.ReceiveAsync<FlowResult<EventExpectationResult>>(
                    Output,
                    Timeout);
                clock.Advance(TimeSpan.FromMilliseconds(500));

                var result = (await resultReceive).Message.ShouldNotBeNull().Payload;
                result.Kind.ShouldBe(ExpectationResultKinds.TimedOut);
                result.IsError.ShouldBeFalse();
                var value = result.Value.ShouldNotBeNull();
                value.Kind.ShouldBe(EventExpectationResultKind.Guard);
                value.Satisfied.ShouldBeTrue();
                value.Matched.ShouldBeFalse();
                value.TimedOut.ShouldBeTrue();
                value.EvaluatedAt.ShouldBe(clock.GetUtcNow());
            },
            Properties(
                ("kind", EventExpectationNodeKind.Guard),
                ("timeoutMilliseconds", 500),
                ("filter", new EventFilter { Status = "failed" })),
            clock);
    }

    [Fact]
    public async Task Canonical_host_exposes_correlated_events()
    {
        await WithNodeAsync(
            async (ports, _) =>
            {
                var eventReceive = ports.ReceiveAsync<CompositionComponentEvent>(
                    Events,
                    Timeout);
                var message = FlowMessage.Create(CreateEvent(
                    DateTimeOffset.Parse("2026-06-18T13:30:00Z"),
                    "job.finished"));

                (await ports.SendAsync(Input, message)).IsAccepted.ShouldBeTrue();

                var @event = (await eventReceive).Message.ShouldNotBeNull();
                @event.CorrelationId.ShouldBe(message.CorrelationId);
                @event.Payload.Name.ShouldBe(ExpectationDiagnosticNames.Matched);
                @event.Payload.Attributes["satisfied"].GetBoolean().ShouldBeTrue();
                @event.Payload.Attributes["isError"].GetBoolean().ShouldBeFalse();
            },
            Properties(("filter", new EventFilter { Type = "job.finished" })));
    }

    [Fact]
    public async Task Canonical_host_emits_evaluation_failure_as_normal_result()
    {
        await WithNodeAsync(
            async (ports, _) =>
            {
                var resultReceive = ports.ReceiveAsync<FlowResult<EventExpectationResult>>(
                    Output,
                    Timeout);
                var bad = FlowMessage.Create(
                    new ProjectionEvent
                    {
                        Timestamp = DateTimeOffset.Parse("2026-06-18T14:00:00Z"),
                        Type = "job.finished",
                        Source = "processor",
                        Attributes = new ThrowingDictionary()
                    },
                    new CorrelationId("bad"));

                (await ports.SendAsync(Input, bad)).IsAccepted.ShouldBeTrue();
                var result = (await resultReceive).Message.ShouldNotBeNull();

                result.CorrelationId.ShouldBe(bad.CorrelationId);
                result.CausationId.ShouldBe(bad.MessageId);
                result.Payload.Kind.ShouldBe(ExpectationResultKinds.EvaluationFailed);
                result.Payload.IsError.ShouldBeTrue();
                result.Payload.Error.ShouldNotBeNull().Code
                    .ShouldBe(ExpectationErrorCodeNames.EvaluationFailed);
                result.Payload.Value.ShouldBeNull();
            },
            Properties(("filter", new EventFilter
            {
                Type = "job.finished",
                Attributes = new Dictionary<string, string>
                {
                    ["k"] = "v"
                }
            })));
    }

    [Fact]
    public async Task Canonical_host_accepts_hidden_legacy_type_alias()
    {
        await using var host = await StartHostAsync(
            Properties(("filter", new EventFilter { Type = "match" })),
            componentType: ExpectationsCompositionNodeTypes.LegacyEventExpectation);

        host.StartResult.Succeeded.ShouldBeTrue();
        var ports = host.GetRequiredPorts();
        var resultReceive = ports.ReceiveAsync<FlowResult<EventExpectationResult>>(
            Output,
            Timeout);
        (await ports.SendAsync(Input, FlowMessage.Create(CreateEvent(
            DateTimeOffset.Parse("2026-06-18T14:30:00Z"),
            "match")))).IsAccepted.ShouldBeTrue();
        (await resultReceive).Message.ShouldNotBeNull()
            .Payload.Kind.ShouldBe(ExpectationResultKinds.Matched);
    }

    [Theory]
    [InlineData("timeoutMilliseconds", 0, "timeoutMilliseconds")]
    [InlineData("maxObservedEvents", -1, "maxObservedEvents")]
    [InlineData("maxPreviewChars", -1, "maxPreviewChars")]
    [InlineData("boundedCapacity", 0, "capacity")]
    public async Task Invalid_configuration_surfaces_preparation_failure(
        string optionName,
        object value,
        string expectedMessage)
    {
        await using var host = await StartHostAsync(Properties((optionName, value)));

        AssertPreparationFailure(host, expectedMessage);
    }

    private static ComponentDesignMetadata DesignMetadata()
        => new ExpectationsComponentDesignMetadataProvider()
            .GetMetadata()
            .ShouldHaveSingleItem();

    private static async Task WithNodeAsync(
        Func<ApplicationPortRuntime, CanonicalApplicationTestHost, Task> run,
        IReadOnlyDictionary<string, object?> properties,
        TimeProvider? clock = null)
    {
        await using var host = await StartHostAsync(properties, clock);
        host.StartResult.Succeeded.ShouldBeTrue();

        await run(host.GetRequiredPorts(), host);
    }

    private static ValueTask<CanonicalApplicationTestHost> StartHostAsync(
        IReadOnlyDictionary<string, object?> properties,
        TimeProvider? clock = null,
        string componentType = ExpectationsCompositionNodeTypes.EventExpectation)
    {
        var componentProperties = properties.ToDictionary(
            static property => property.Key,
            static property => property.Value,
            StringComparer.Ordinal);
        IReadOnlyList<string>? resources = null;
        if (clock is not null)
        {
            componentProperties[ExpectationsCompositionResourceNames.Clock] = "Resources.clock";
            resources = ["clock"];
        }

        return CanonicalApplicationTestHost.StartAsync(
            SingleComponent(componentType, componentProperties, resources),
            registry => registry.RegisterEventExpectation(),
            configureRuntimeServices: context =>
            {
                if (clock is not null)
                {
                    context.Services.AddExternalFluxFlowResource<TimeProvider>(
                        ApplicationAddress.Resource("clock"),
                        clock);
                }
            });
    }

    private static void AssertOption(
        ComponentDesignMetadata metadata,
        string name,
        OptionValueKind kind,
        object? defaultValue = null,
        double? min = null)
    {
        var option = metadata.Options.Single(option => option.Name.Value == name);
        option.Kind.ShouldBe(kind);
        option.DefaultValue.ShouldBe(defaultValue);
        option.Min.ShouldBe(min);
    }

    private static void AssertOptionHints(
        OptionDesignMetadata option,
        string section,
        string importance,
        string? editor = null)
    {
        AttributeValue(option.Attributes, OptionDesignMetadataAttributeNames.Section)
            .ShouldBe(section);
        AttributeValue(option.Attributes, OptionDesignMetadataAttributeNames.Importance)
            .ShouldBe(importance);

        if (editor is null)
        {
            option.Attributes.ContainsKey(
                new ComponentAttributeName(OptionDesignMetadataAttributeNames.Editor))
                .ShouldBeFalse();
        }
        else
        {
            AttributeValue(option.Attributes, OptionDesignMetadataAttributeNames.Editor)
                .ShouldBe(editor);
        }

        option.Attributes.ContainsKey(
            new ComponentAttributeName(OptionDesignMetadataAttributeNames.Syntax))
            .ShouldBeFalse();
        option.Attributes.ContainsKey(
            new ComponentAttributeName(OptionDesignMetadataAttributeNames.RelatedResource))
            .ShouldBeFalse();
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

    private static void AssertResourceHints(
        ResourceDesignMetadata resource,
        string pickerKind,
        string keyPattern)
    {
        AttributeValue(resource.Attributes, ResourceDesignMetadataAttributeNames.Ownership)
            .ShouldBe(ResourceDesignMetadataAttributeValues.HostOwned);
        AttributeValue(resource.Attributes, ResourceDesignMetadataAttributeNames.PickerKind)
            .ShouldBe(pickerKind);
        AttributeValue(resource.Attributes, ResourceDesignMetadataAttributeNames.KeyPattern)
            .ShouldBe(keyPattern);
    }

    private static string AttributeValue(
        IReadOnlyDictionary<ComponentAttributeName, ComponentAttributeValue> attributes,
        string name)
        => attributes[new ComponentAttributeName(name)].Value;

    private static void AssertPreparationFailure(
        CanonicalApplicationTestHost host,
        string expectedMessage)
    {
        host.StartResult.Succeeded.ShouldBeFalse();
        host.StartResult.Update!.Status.ShouldBe(ApplicationRevisionUpdateStatus.Rejected);
        host.StartResult.Update.Failures.ShouldContain(failure =>
            failure.Stage == ApplicationRevisionFailureStage.Preparation &&
            failure.Error.Details.GetObject()["exceptionMessage"].GetString().Contains(
                expectedMessage,
                StringComparison.OrdinalIgnoreCase));
        host.RuntimeAccess.Ports.ShouldBeNull();
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
        public bool TryGetValue(string key, out string value)
            => throw new InvalidOperationException("boom");
        public IEnumerator<KeyValuePair<string, string>> GetEnumerator()
            => throw new InvalidOperationException("boom");
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
