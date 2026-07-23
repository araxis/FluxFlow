using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.Sources.Composition;
using FluxFlow.Components.Sources.Nodes;
using FluxFlow.Composition;
using FluxFlow.Composition.Addressing;
using FluxFlow.Composition.Hosting.DependencyInjection;
using FluxFlow.Composition.Hosting.Revisions;
using FluxFlow.Data;
using FluxFlow.Engine.Ports;
using FluxFlow.Nodes;
using FluxFlow.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;
using static FluxFlow.Testing.CanonicalTestApplication;

namespace FluxFlow.Components.Sources.Composition.Tests;

public sealed class SourcesCompositionNodeRegistryExtensionsTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private static readonly ApplicationAddress Output =
        ApplicationAddress.WorkflowPort(
            "main",
            "source",
            SourcesCompositionPortNames.Output);
    private static readonly ApplicationAddress Events =
        ApplicationAddress.WorkflowPort(
            "main",
            "source",
            CompositionComponentEvents.PortName);

    [Fact]
    public void Register_source_nodes_exposes_only_canonical_flowvalue_metadata()
    {
        var registry = new CompositionNodeRegistry()
            .RegisterGeneratedSource()
            .RegisterSequenceSource();

        registry.Registrations[SourcesCompositionNodeTypes.Generated]
            .Outputs[SourcesCompositionPortNames.Output].MessageType.ShouldBe(
                typeof(FlowValue));
        registry.Registrations[SourcesCompositionNodeTypes.Sequence]
            .Outputs[SourcesCompositionPortNames.Output].MessageType.ShouldBe(
                typeof(FlowValue));
        typeof(SourcesCompositionNodeRegistryExtensions).GetMethods()
            .ShouldNotContain(static method => method.IsGenericMethodDefinition);
    }

    [Fact]
    public void Register_source_nodes_supports_explicit_canonical_component_types()
    {
        var registry = new CompositionNodeRegistry()
            .RegisterGeneratedSource("source.items.orders")
            .RegisterGeneratedSource("source.items.audit")
            .RegisterSequenceSource("source.sequence.custom");

        registry.Registrations.Keys.ShouldBe([
            "source.items.orders",
            "source.items.audit",
            "source.sequence.custom"
        ], ignoreOrder: false);
        registry.Registrations.Values.ShouldAllBe(registration =>
            registration.Outputs[SourcesCompositionPortNames.Output].MessageType ==
                typeof(FlowValue));
    }

    [Fact]
    public void Register_generated_source_preserves_the_explicit_migration_alias()
    {
        var registry = new CompositionNodeRegistry().RegisterGeneratedSource();

        registry.TryResolveType(
            SourcesCompositionNodeTypes.LegacyGenerated,
            out var canonicalType).ShouldBeTrue();
        canonicalType.ShouldBe(SourcesCompositionNodeTypes.Generated);
    }

    [Fact]
    public void Design_metadata_provider_returns_valid_source_metadata()
    {
        var metadata = new SourcesComponentDesignMetadataProvider().GetMetadata();

        metadata.Select(item => item.Type.Value).ShouldBe([
            SourcesCompositionNodeTypes.Generated,
            SourcesCompositionNodeTypes.Sequence
        ]);
        metadata.SelectMany(ComponentDesignMetadataValidator.Validate).ShouldBeEmpty();
        metadata.SelectMany(item => item.Options)
            .Select(option => option.Name.Value)
            .ShouldNotContain(SourcesCompositionResourceNames.Clock);
        foreach (var item in metadata)
            AssertClockResource(item);

        metadata.Single(item =>
                item.Type.Value == SourcesCompositionNodeTypes.Generated)
            .Attributes.ContainsKey(new ComponentAttributeName("omittedOptions"))
            .ShouldBeFalse();
    }

    [Fact]
    public void Design_metadata_provider_describes_source_ports()
    {
        var metadata = MetadataByType();

        AssertSourcePorts(metadata[SourcesCompositionNodeTypes.Generated]);
        AssertSourcePorts(metadata[SourcesCompositionNodeTypes.Sequence]);
    }

    [Fact]
    public void Design_metadata_provider_describes_source_options()
    {
        var metadata = MetadataByType();

        AssertOptions(
            metadata[SourcesCompositionNodeTypes.Generated],
            [
                ("name", OptionValueKind.Text, "generated", null),
                ("items", OptionValueKind.Json, null, null),
                ("loop", OptionValueKind.Boolean, false, null),
                ("maxItems", OptionValueKind.Number, null, 1),
                ("initialDelayMilliseconds", OptionValueKind.Number, 0, 0),
                ("intervalMilliseconds", OptionValueKind.Number, 0, 0),
                ("boundedCapacity", OptionValueKind.Number, 128, 1)
            ]);
        AssertOptions(
            metadata[SourcesCompositionNodeTypes.Sequence],
            [
                ("name", OptionValueKind.Text, "sequence", null),
                ("start", OptionValueKind.Number, 1, null),
                ("step", OptionValueKind.Number, 1, null),
                ("count", OptionValueKind.Number, 1, 1),
                ("initialDelayMilliseconds", OptionValueKind.Number, 0, 0),
                ("intervalMilliseconds", OptionValueKind.Number, 0, 0),
                ("boundedCapacity", OptionValueKind.Number, 128, 1)
            ]);
    }

    [Fact]
    public void Design_metadata_provider_describes_source_option_hints()
    {
        var metadata = MetadataByType();
        var generated = OptionsByName(metadata[SourcesCompositionNodeTypes.Generated]);
        var sequence = OptionsByName(metadata[SourcesCompositionNodeTypes.Sequence]);

        AssertOptionHints(generated["name"], "Diagnostics", "advanced", "text");
        AssertOptionHints(generated["items"], "Items", "primary", "json");
        AssertOptionHints(generated["loop"], "Emission", "advanced");
        AssertOptionHints(generated["maxItems"], "Runtime", "advanced", "number");
        AssertOptionHints(
            generated["initialDelayMilliseconds"],
            "Timing",
            "advanced",
            "number");
        AssertOptionHints(
            generated["intervalMilliseconds"],
            "Timing",
            "advanced",
            "number");
        AssertOptionHints(
            generated["boundedCapacity"],
            "Runtime",
            "advanced",
            "number");

        AssertOptionHints(sequence["name"], "Diagnostics", "advanced", "text");
        AssertOptionHints(sequence["start"], "Sequence", "advanced", "number");
        AssertOptionHints(sequence["step"], "Sequence", "advanced", "number");
        AssertOptionHints(sequence["count"], "Sequence", "primary", "number");
        AssertOptionHints(
            sequence["initialDelayMilliseconds"],
            "Timing",
            "advanced",
            "number");
        AssertOptionHints(
            sequence["intervalMilliseconds"],
            "Timing",
            "advanced",
            "number");
        AssertOptionHints(
            sequence["boundedCapacity"],
            "Runtime",
            "advanced",
            "number");
    }

    [Fact]
    public void Design_metadata_provider_uses_canonical_resource_picker_hints()
    {
        foreach (var item in MetadataByType().Values)
        {
            AssertResourceHints(
                item.Resources.ShouldHaveSingleItem(),
                ResourceDesignMetadataAttributeValues.Clock,
                "Resources.{name}");
        }
    }

    [Fact]
    public void Design_metadata_provider_loads_into_catalog()
    {
        var catalog = ComponentDesignMetadataCatalog.FromProviders(
            [new SourcesComponentDesignMetadataProvider()]);

        catalog.All.Count.ShouldBe(2);
        catalog.TryGet(
            new ComponentType(SourcesCompositionNodeTypes.Generated),
            out var generated).ShouldBeTrue();
        generated.ShouldNotBeNull();
        generated.Type.ShouldBe(new ComponentType(SourcesCompositionNodeTypes.Generated));
    }

    [Fact]
    public async Task Hosted_generated_source_binds_items_and_emits_events()
    {
        var clock = NewClock();
        var scheduled = clock.TimerScheduled;
        await using var host = await StartHostAsync(
            SourcesCompositionNodeTypes.Generated,
            Properties(
                ("name", "orders"),
                ("items", new[]
                {
                    new Dictionary<string, object?>
                    {
                        ["id"] = "A-100",
                        ["value"] = 10
                    },
                    new Dictionary<string, object?>
                    {
                        ["id"] = "A-101",
                        ["value"] = 20
                    }
                }),
                ("initialDelayMilliseconds", 10),
                ("boundedCapacity", 8)),
            clock);
        host.StartResult.Succeeded.ShouldBeTrue();
        await scheduled.WaitAsync(Timeout);
        var ports = host.GetRequiredPorts();
        var output = (await ports.ObserveAsync<FlowValue>(Output))
            .Observation.ShouldNotBeNull();
        await using var outputObservation = output;
        var events = (await ports.ObserveAsync<CompositionComponentEvent>(Events))
            .Observation.ShouldNotBeNull();
        await using var eventObservation = events;

        clock.Advance(TimeSpan.FromMilliseconds(10));
        var first = await output.Messages.ReceiveAsync().WaitAsync(Timeout);
        var second = await output.Messages.ReceiveAsync().WaitAsync(Timeout);
        var eventNames = await ReceiveEventsThroughAsync(
            events.Messages,
            GeneratedSourceNode.Completed);

        first.Payload.GetObject()["id"].GetString().ShouldBe("A-100");
        first.Payload.GetObject()["value"].GetInteger().ShouldBe(10);
        second.Payload.GetObject()["id"].GetString().ShouldBe("A-101");
        second.Payload.GetObject()["value"].GetInteger().ShouldBe(20);
        first.CorrelationId.ShouldNotBe(second.CorrelationId);
        eventNames.ShouldContain(GeneratedSourceNode.Emitted);
        eventNames.ShouldContain(GeneratedSourceNode.Completed);
    }

    [Fact]
    public async Task Hosted_generated_source_normalizes_one_scalar_item()
    {
        var clock = NewClock();
        var scheduled = clock.TimerScheduled;
        await using var host = await StartHostAsync(
            SourcesCompositionNodeTypes.Generated,
            Properties(
                ("items", "one"),
                ("initialDelayMilliseconds", 10)),
            clock);
        host.StartResult.Succeeded.ShouldBeTrue();
        await scheduled.WaitAsync(Timeout);
        var output = (await host.GetRequiredPorts().ObserveAsync<FlowValue>(Output))
            .Observation.ShouldNotBeNull();
        await using var observation = output;

        clock.Advance(TimeSpan.FromMilliseconds(10));
        var emitted = await output.Messages.ReceiveAsync().WaitAsync(Timeout);

        emitted.Payload.GetString().ShouldBe("one");
    }

    [Fact]
    public async Task Hosted_generated_source_missing_items_completes_empty()
    {
        var clock = NewClock();
        var scheduled = clock.TimerScheduled;
        await using var host = await StartHostAsync(
            SourcesCompositionNodeTypes.Generated,
            Properties(("initialDelayMilliseconds", 10)),
            clock);
        host.StartResult.Succeeded.ShouldBeTrue();
        await scheduled.WaitAsync(Timeout);
        var ports = host.GetRequiredPorts();
        var events = (await ports.ObserveAsync<CompositionComponentEvent>(Events))
            .Observation.ShouldNotBeNull();
        await using var eventObservation = events;

        clock.Advance(TimeSpan.FromMilliseconds(10));
        await ReceiveEventsThroughAsync(events.Messages, GeneratedSourceNode.Completed);
    }

    [Fact]
    public async Task Hosted_sequence_source_binds_settings_and_exact_clock_resource()
    {
        var startedAt = new DateTimeOffset(2026, 6, 2, 12, 0, 0, TimeSpan.Zero);
        var clock = new TrackingFakeTimeProvider(startedAt);
        var firstScheduled = clock.TimerScheduled;
        await using var host = await StartHostAsync(
            SourcesCompositionNodeTypes.Sequence,
            Properties(
                ("name", "numbers"),
                ("start", 10),
                ("step", 5),
                ("count", 3),
                ("initialDelayMilliseconds", 10),
                ("intervalMilliseconds", 25)),
            clock);
        host.StartResult.Succeeded.ShouldBeTrue();
        await firstScheduled.WaitAsync(Timeout);
        var output = (await host.GetRequiredPorts().ObserveAsync<FlowValue>(Output))
            .Observation.ShouldNotBeNull();
        await using var observation = output;

        clock.Advance(TimeSpan.FromMilliseconds(10));
        var first = await output.Messages.ReceiveAsync().WaitAsync(Timeout);
        await clock.WaitForTimerCountAsync(2);
        clock.Advance(TimeSpan.FromMilliseconds(25));
        var second = await output.Messages.ReceiveAsync().WaitAsync(Timeout);
        await clock.WaitForTimerCountAsync(3);
        clock.Advance(TimeSpan.FromMilliseconds(25));
        var third = await output.Messages.ReceiveAsync().WaitAsync(Timeout);

        var messages = new[] { first, second, third };
        messages.Select(message => message.Payload.GetObject()["sequence"].GetInteger())
            .ShouldBe([1, 2, 3]);
        messages.Select(message => message.Payload.GetObject()["value"].GetInteger())
            .ShouldBe([10, 15, 20]);
        messages.ShouldAllBe(message =>
            message.Payload.GetObject()["name"].GetString() == "numbers");
        messages.ShouldAllBe(message =>
            message.Payload.GetObject()["timestamp"].GetDateTimeOffset() >= startedAt);
        messages.Select(message => message.CorrelationId).Distinct().Count().ShouldBe(3);
    }

    [Theory]
    [InlineData(SourcesCompositionNodeTypes.Generated, "boundedCapacity", 0, "capacity")]
    [InlineData(SourcesCompositionNodeTypes.Generated, "initialDelayMilliseconds", -1, "initialDelayMilliseconds")]
    [InlineData(SourcesCompositionNodeTypes.Generated, "intervalMilliseconds", -1, "intervalMilliseconds")]
    [InlineData(SourcesCompositionNodeTypes.Generated, "maxItems", 0, "maxItems")]
    [InlineData(SourcesCompositionNodeTypes.Generated, "loop", true, "maxItems")]
    [InlineData(SourcesCompositionNodeTypes.Sequence, "boundedCapacity", 0, "boundedCapacity")]
    [InlineData(SourcesCompositionNodeTypes.Sequence, "initialDelayMilliseconds", -1, "initialDelayMilliseconds")]
    [InlineData(SourcesCompositionNodeTypes.Sequence, "intervalMilliseconds", -1, "intervalMilliseconds")]
    [InlineData(SourcesCompositionNodeTypes.Sequence, "count", 0, "count")]
    [InlineData(SourcesCompositionNodeTypes.Sequence, "step", 0L, "step")]
    public async Task Invalid_source_configuration_rejects_canonical_revision(
        string componentType,
        string optionName,
        object value,
        string expectedMessage)
    {
        var properties = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [optionName] = value
        };
        if (componentType == SourcesCompositionNodeTypes.Generated)
            properties["items"] = new[] { "one" };

        await using var host = await StartHostAsync(componentType, properties);

        AssertPreparationFailure(host, expectedMessage);
    }

    private static Dictionary<string, ComponentDesignMetadata> MetadataByType()
        => new SourcesComponentDesignMetadataProvider()
            .GetMetadata()
            .ToDictionary(item => item.Type.Value, StringComparer.Ordinal);

    private static Dictionary<string, OptionDesignMetadata> OptionsByName(
        ComponentDesignMetadata metadata)
        => metadata.Options.ToDictionary(
            option => option.Name.Value,
            StringComparer.Ordinal);

    private static void AssertSourcePorts(ComponentDesignMetadata metadata)
    {
        metadata.Ports.Select(port => (
            port.Name.Value,
            port.Direction,
            port.Order,
            port.IsPrimary,
            port.ValueType?.Value)).ShouldBe([
            (SourcesCompositionPortNames.Output, PortDirection.Output, 0, true, nameof(FlowValue))
        ]);
    }

    private static void AssertClockResource(ComponentDesignMetadata metadata)
    {
        var resource = metadata.Resources.ShouldHaveSingleItem();
        resource.Name.Value.ShouldBe(SourcesCompositionResourceNames.Clock);
        resource.DisplayName?.Value.ShouldBe("Clock");
        resource.Order.ShouldBe(0);
        resource.IsRequired.ShouldBeFalse();
        resource.ValueType?.Value.ShouldBe(nameof(TimeProvider));
    }

    private static void AssertOptions(
        ComponentDesignMetadata metadata,
        IReadOnlyList<(string Name, OptionValueKind Kind, object? DefaultValue, double? Min)> expected)
    {
        metadata.Options.Select(option => (
            option.Name.Value,
            option.Kind,
            option.DefaultValue,
            option.Min)).ShouldBe(expected);
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

        var editorName = new ComponentAttributeName(
            OptionDesignMetadataAttributeNames.Editor);
        if (editor is null)
            option.Attributes.ContainsKey(editorName).ShouldBeFalse();
        else
            AttributeValue(option.Attributes, editorName.Value).ShouldBe(editor);

        option.Attributes.ContainsKey(new ComponentAttributeName(
            OptionDesignMetadataAttributeNames.Syntax)).ShouldBeFalse();
        option.Attributes.ContainsKey(new ComponentAttributeName(
            OptionDesignMetadataAttributeNames.RelatedResource)).ShouldBeFalse();
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

    private static ValueTask<CanonicalApplicationTestHost> StartHostAsync(
        string componentType,
        IReadOnlyDictionary<string, object?> properties,
        TimeProvider? clock = null)
    {
        var componentProperties = properties.ToDictionary(
            static property => property.Key,
            static property => property.Value,
            StringComparer.Ordinal);
        IReadOnlyList<string>? resources = null;
        if (clock is not null)
        {
            componentProperties[SourcesCompositionResourceNames.Clock] = "Resources.fixed";
            resources = ["fixed"];
        }

        return CanonicalApplicationTestHost.StartAsync(
            SingleComponent(
                componentType,
                componentProperties,
                resources,
                componentName: "source"),
            RegisterAll,
            configureRuntimeServices: context =>
            {
                if (clock is not null)
                {
                    context.Services.AddExternalFluxFlowResource<TimeProvider>(
                        ApplicationAddress.Resource("fixed"),
                        clock);
                }
            });
    }

    private static void RegisterAll(CompositionNodeRegistry registry)
        => registry
            .RegisterGeneratedSource()
            .RegisterSequenceSource();

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

    private static async Task<IReadOnlyList<string>> ReceiveEventsThroughAsync(
        ISourceBlock<FlowMessage<CompositionComponentEvent>> messages,
        string terminalEvent)
    {
        var names = new List<string>();
        while (!names.Contains(terminalEvent, StringComparer.Ordinal))
        {
            var message = await messages.ReceiveAsync().WaitAsync(Timeout);
            names.Add(message.Payload.Name);
        }

        return names;
    }

    private static TrackingFakeTimeProvider NewClock()
        => new(new DateTimeOffset(2026, 6, 2, 12, 0, 0, TimeSpan.Zero));

    private sealed class TrackingFakeTimeProvider : FakeTimeProvider
    {
        private readonly object _gate = new();
        private int _createdCount;
        private TaskCompletionSource _nextTimer = CreateSource();

        public TrackingFakeTimeProvider(DateTimeOffset startDateTime)
            : base(startDateTime)
        {
        }

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            var timer = base.CreateTimer(callback, state, dueTime, period);
            TaskCompletionSource signalled;
            lock (_gate)
            {
                _createdCount++;
                signalled = _nextTimer;
                _nextTimer = CreateSource();
            }

            signalled.TrySetResult();
            return timer;
        }

        public Task TimerScheduled
        {
            get
            {
                lock (_gate)
                    return _nextTimer.Task;
            }
        }

        public async Task WaitForTimerCountAsync(int expected)
        {
            while (true)
            {
                Task scheduled;
                lock (_gate)
                {
                    if (_createdCount >= expected)
                        return;

                    scheduled = _nextTimer.Task;
                }

                await scheduled.WaitAsync(Timeout);
            }
        }

        private static TaskCompletionSource CreateSource()
            => new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
