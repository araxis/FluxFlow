using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.Sources.Composition;
using FluxFlow.Components.Sources.Contracts;
using FluxFlow.Components.Sources.Nodes;
using FluxFlow.Composition;
using FluxFlow.Composition.Addressing;
using FluxFlow.Composition.Authoring;
using FluxFlow.Composition.DependencyInjection;
using FluxFlow.Engine;
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

public sealed class SourcesServiceCollectionExtensionsTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private static readonly ApplicationAddress Output =
        ApplicationAddress.WorkflowPort(
            "main",
            "source",
            SourcesComponentDefinition.Ports.Output);
    private static readonly ApplicationAddress Events =
        ApplicationAddress.WorkflowPort(
            "main",
            "source",
            "Events");

    [Fact]
    public void AddSources_exposes_canonical_typed_metadata()
    {
        var registry = ComponentCatalogTestHost.Create(
            services => services.AddFluxFlowComponents().AddSources());

        registry.Components[SourcesComponentDefinition.Types.Generated]
            .Outputs[SourcesComponentDefinition.Ports.Output].MessageType.ShouldBe(
                typeof(JsonElement));
        registry.Components[SourcesComponentDefinition.Types.Sequence]
            .Outputs[SourcesComponentDefinition.Ports.Output].MessageType.ShouldBe(
                typeof(SequenceItem));
        typeof(SourcesServiceCollectionExtensions).GetMethods()
            .ShouldNotContain(static method => method.IsGenericMethodDefinition);
    }

    [Fact]
    public void AddSources_is_idempotent()
    {
        var catalog = ComponentCatalogTestHost.Create(services =>
        {
            services.AddFluxFlowComponents().AddSources();
            services.AddFluxFlowComponents().AddSources();
        });

        catalog.Components.Keys.ShouldBe([
            SourcesComponentDefinition.Types.Generated,
            SourcesComponentDefinition.Types.Sequence
        ]);
    }

    [Fact]
    public void AddSources_rejects_the_obsolete_component_type()
    {
        var registry = ComponentCatalogTestHost.Create(
            services => services.AddFluxFlowComponents().AddSources());

        registry.TryGetDescriptor("source.generated", out _).ShouldBeFalse();
    }

    [Fact]
    public void Design_metadata_provider_returns_valid_source_metadata()
    {
        var metadata = DesignMetadata();

        metadata.Select(item => item.Type.Value).ShouldBe([
            SourcesComponentDefinition.Types.Generated,
            SourcesComponentDefinition.Types.Sequence
        ]);
        metadata.SelectMany(ComponentDesignMetadataValidator.Validate).ShouldBeEmpty();
        metadata.SelectMany(item => item.Options)
            .Select(option => option.Name.Value)
            .ShouldNotContain(SourcesComponentDefinition.Resources.Clock);
        foreach (var item in metadata)
            AssertClockResource(item);

        metadata.Single(item =>
                item.Type.Value == SourcesComponentDefinition.Types.Generated)
            .Attributes.ContainsKey(new ComponentAttributeName("omittedOptions"))
            .ShouldBeTrue();
    }

    [Fact]
    public void Design_metadata_provider_describes_source_ports()
    {
        var metadata = MetadataByType();

        AssertSourcePorts(metadata[SourcesComponentDefinition.Types.Generated], nameof(JsonElement));
        AssertSourcePorts(metadata[SourcesComponentDefinition.Types.Sequence], nameof(SequenceItem));
    }

    [Fact]
    public void Design_metadata_provider_describes_source_options()
    {
        var metadata = MetadataByType();

        AssertOptions(
            metadata[SourcesComponentDefinition.Types.Generated],
            [
                ("items", OptionValueKind.Json, null, null),
                ("loop", OptionValueKind.Boolean, false, null),
                ("maxItems", OptionValueKind.Number, null, 1),
                ("initialDelayMilliseconds", OptionValueKind.Number, 0, 0),
                ("intervalMilliseconds", OptionValueKind.Number, 0, 0),
                ("boundedCapacity", OptionValueKind.Number, 128, 1),
                ("processing", OptionValueKind.Text, null, null)
            ]);
        AssertOptions(
            metadata[SourcesComponentDefinition.Types.Sequence],
            [
                ("start", OptionValueKind.Number, 1, null),
                ("step", OptionValueKind.Number, 1, null),
                ("count", OptionValueKind.Number, 1, 1),
                ("initialDelayMilliseconds", OptionValueKind.Number, 0, 0),
                ("intervalMilliseconds", OptionValueKind.Number, 0, 0),
                ("boundedCapacity", OptionValueKind.Number, 128, 1),
                ("processing", OptionValueKind.Text, null, null)
            ]);
    }

    [Fact]
    public void Design_metadata_provider_describes_source_option_hints()
    {
        var metadata = MetadataByType();
        var generated = OptionsByName(metadata[SourcesComponentDefinition.Types.Generated]);
        var sequence = OptionsByName(metadata[SourcesComponentDefinition.Types.Sequence]);

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
        AssertOptionHints(generated["boundedCapacity"], "Runtime", "advanced", "number");
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
        AssertOptionHints(sequence["boundedCapacity"], "Runtime", "advanced", "number");
    }

    [Fact]
    public void Design_metadata_provider_uses_canonical_resource_picker_hints()
    {
        foreach (var item in MetadataByType().Values)
        {
            AssertResourceHints(
                item.Resources.Single(resource =>
                    resource.Name.Value == SourcesComponentDefinition.Resources.Clock),
                ResourceDesignMetadataAttributeValues.Clock,
                "Resources.{name}");
        }
    }

    [Fact]
    public void Design_metadata_provider_loads_into_catalog()
    {
        var catalog = ComponentCatalogTestHost.CreateDesignMetadataCatalog(
            static services => services.AddFluxFlowComponents().AddSources());

        catalog.All.Count.ShouldBe(2);
        catalog.TryGet(
            new ComponentType(SourcesComponentDefinition.Types.Generated),
            out var generated).ShouldBeTrue();
        generated.ShouldNotBeNull();
        generated.Type.ShouldBe(new ComponentType(SourcesComponentDefinition.Types.Generated));
    }

    [Fact]
    public async Task Hosted_generated_source_binds_items_and_emits_events()
    {
        var clock = NewClock();
        var scheduled = clock.TimerScheduled;
        await using var host = await StartHostAsync(
            SourcesComponentDefinition.Types.Generated,
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
        var output = (await ports.ObserveAsync<JsonElement>(Output))
            .Observation.ShouldNotBeNull();
        await using var outputObservation = output;
        var events = (await ports.ObserveAsync<ComponentEvent>(Events))
            .Observation.ShouldNotBeNull();
        await using var eventObservation = events;

        clock.Advance(TimeSpan.FromMilliseconds(10));
        var first = await output.Messages.ReceiveAsync().WaitAsync(Timeout);
        var second = await output.Messages.ReceiveAsync().WaitAsync(Timeout);
        var eventNames = await ReceiveEventsThroughAsync(
            events.Messages,
            GeneratedSourceNode.Completed);

        first.Value.GetProperty("id").GetString().ShouldBe("A-100");
        first.Value.GetProperty("value").GetInt32().ShouldBe(10);
        second.Value.GetProperty("id").GetString().ShouldBe("A-101");
        second.Value.GetProperty("value").GetInt32().ShouldBe(20);
        first.MessageId.ShouldNotBe(second.MessageId);
        eventNames.ShouldContain(GeneratedSourceNode.Emitted);
        eventNames.ShouldContain(GeneratedSourceNode.Completed);
    }

    [Fact]
    public async Task Typed_generated_source_authoring_writes_canonical_capacity_and_binds_runtime_options()
    {
        var builder = new ApplicationDefinitionBuilder();
        var clockResource = builder.AddResource<TimeProvider>("fixed", "host.clock");
        builder.AddWorkflow("main").AddGeneratedSource("source", source =>
        {
            source.SetItems(new[] { "one" });
            source.InitialDelayMilliseconds = 10;
            source.BoundedCapacity = 3;
            source.Clock = clockResource;
        });
        var definition = builder.Build();
        var component = definition.Workflows["main"].Components["source"];
        var clock = NewClock();
        var scheduled = clock.TimerScheduled;

        component.Type.ShouldBe(SourcesComponentDefinition.Types.Generated);
        component.Properties[SourcesComponentDefinition.Options.BoundedCapacity]
            .GetInt32().ShouldBe(3);
        component.Properties.ContainsKey("BoundedCapacity").ShouldBeFalse();

        await using var host = await CanonicalApplicationTestHost.StartAsync(
            definition,
            AddSources,
            registerResources: context =>
                context.Services.AddExternalFluxFlowResource<TimeProvider>(
                    clockResource.Address,
                    clock));
        host.StartResult.Succeeded.ShouldBeTrue();
        await scheduled.WaitAsync(Timeout);
        var ports = host.GetRequiredPorts();
        var output = (await ports.ObserveAsync<JsonElement>(Output))
            .Observation.ShouldNotBeNull();
        await using var outputObservation = output;
        var events = (await ports.ObserveAsync<ComponentEvent>(Events))
            .Observation.ShouldNotBeNull();
        await using var eventObservation = events;
        var emittedEvent = ReceiveEventAsync(events.Messages, GeneratedSourceNode.Emitted);

        clock.Advance(TimeSpan.FromMilliseconds(10));

        (await output.Messages.ReceiveAsync().WaitAsync(Timeout)).Value.GetString()
            .ShouldBe("one");
        (await emittedEvent).Attributes["boundedCapacity"].ShouldBe("3");
    }

    [Fact]
    public async Task Hosted_generated_source_normalizes_one_scalar_item()
    {
        var clock = NewClock();
        var scheduled = clock.TimerScheduled;
        await using var host = await StartHostAsync(
            SourcesComponentDefinition.Types.Generated,
            Properties(
                ("items", "one"),
                ("initialDelayMilliseconds", 10)),
            clock);
        host.StartResult.Succeeded.ShouldBeTrue();
        await scheduled.WaitAsync(Timeout);
        var output = (await host.GetRequiredPorts().ObserveAsync<JsonElement>(Output))
            .Observation.ShouldNotBeNull();
        await using var observation = output;

        clock.Advance(TimeSpan.FromMilliseconds(10));
        var emitted = await output.Messages.ReceiveAsync().WaitAsync(Timeout);

        emitted.Value.GetString().ShouldBe("one");
    }

    [Fact]
    public async Task Hosted_generated_source_missing_items_completes_empty()
    {
        var clock = NewClock();
        var scheduled = clock.TimerScheduled;
        await using var host = await StartHostAsync(
            SourcesComponentDefinition.Types.Generated,
            Properties(("initialDelayMilliseconds", 10)),
            clock);
        host.StartResult.Succeeded.ShouldBeTrue();
        await scheduled.WaitAsync(Timeout);
        var ports = host.GetRequiredPorts();
        var events = (await ports.ObserveAsync<ComponentEvent>(Events))
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
            SourcesComponentDefinition.Types.Sequence,
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
        var output = (await host.GetRequiredPorts().ObserveAsync<SequenceItem>(Output))
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
        messages.Select(message => message.Value.Sequence)
            .ShouldBe([1, 2, 3]);
        messages.Select(message => message.Value.Value)
            .ShouldBe([10, 15, 20]);
        messages.ShouldAllBe(message =>
            message.Value.Name == "numbers");
        messages.ShouldAllBe(message =>
            message.Value.Timestamp >= startedAt);
        messages.Select(message => message.MessageId).Distinct().Count().ShouldBe(3);
    }

    [Theory]
    [InlineData(SourcesComponentDefinition.Types.Generated, "boundedCapacity", 0, "capacity")]
    [InlineData(SourcesComponentDefinition.Types.Generated, "initialDelayMilliseconds", -1, "initialDelayMilliseconds")]
    [InlineData(SourcesComponentDefinition.Types.Generated, "intervalMilliseconds", -1, "intervalMilliseconds")]
    [InlineData(SourcesComponentDefinition.Types.Generated, "maxItems", 0, "maxItems")]
    [InlineData(SourcesComponentDefinition.Types.Generated, "loop", true, "maxItems")]
    [InlineData(SourcesComponentDefinition.Types.Sequence, "boundedCapacity", 0, "boundedCapacity")]
    [InlineData(SourcesComponentDefinition.Types.Sequence, "initialDelayMilliseconds", -1, "initialDelayMilliseconds")]
    [InlineData(SourcesComponentDefinition.Types.Sequence, "intervalMilliseconds", -1, "intervalMilliseconds")]
    [InlineData(SourcesComponentDefinition.Types.Sequence, "count", 0, "count")]
    [InlineData(SourcesComponentDefinition.Types.Sequence, "step", 0L, "step")]
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
        if (componentType == SourcesComponentDefinition.Types.Generated)
            properties["items"] = new[] { "one" };

        await using var host = await StartHostAsync(componentType, properties);

        AssertPreparationFailure(host, expectedMessage);
    }

    private static Dictionary<string, ComponentDesignMetadata> MetadataByType()
        => DesignMetadata()
            .ToDictionary(item => item.Type.Value, StringComparer.Ordinal);

    private static IReadOnlyList<ComponentDesignMetadata> DesignMetadata()
        => ComponentCatalogTestHost.CreateDesignMetadataCatalog(
            services => services.AddFluxFlowComponents().AddSources()).All;

    private static Dictionary<string, OptionDesignMetadata> OptionsByName(
        ComponentDesignMetadata metadata)
        => metadata.Options.ToDictionary(
            option => option.Name.Value,
            StringComparer.Ordinal);

    private static void AssertSourcePorts(ComponentDesignMetadata metadata, string valueType)
    {
        metadata.Ports.Select(port => (
            port.Name.Value,
            port.Direction,
            port.Order,
            port.IsPrimary,
            port.ValueType?.Value)).ShouldBe([
            (SourcesComponentDefinition.Ports.Output, PortDirection.Output, 0, true, valueType),
            (SourcesComponentDefinition.Ports.Events, PortDirection.Output, 1, false, nameof(ComponentEvent))
        ]);
    }

    private static void AssertClockResource(ComponentDesignMetadata metadata)
    {
        metadata.Resources.Select(candidate => candidate.Name.Value)
            .ShouldBe([SourcesComponentDefinition.Resources.Clock, "processing"], ignoreOrder: false);
        var resource = metadata.Resources[0];
        resource.Name.Value.ShouldBe(SourcesComponentDefinition.Resources.Clock);
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
            componentProperties[SourcesComponentDefinition.Resources.Clock] = "Resources.fixed";
            resources = ["fixed"];
        }

        return CanonicalApplicationTestHost.StartAsync(
            SingleComponent(
                componentType,
                componentProperties,
                resources,
                componentName: "source"),
            AddSources,
            registerResources: context =>
            {
                if (clock is not null)
                {
                    context.Services.AddExternalFluxFlowResource<TimeProvider>(
                        ApplicationAddress.Resource("fixed"),
                        clock);
                }
            });
    }

    private static void AddSources(IServiceCollection services)
        => services.AddFluxFlowComponents().AddSources();

    private static void AssertPreparationFailure(
        CanonicalApplicationTestHost host,
        string expectedMessage)
    {
        host.StartResult.Succeeded.ShouldBeFalse();
        host.StartResult.Update!.Status.ShouldBe(ApplicationUpdateStatus.Rejected);
        host.StartResult.Update.Diagnostics.ShouldContain(failure =>
            failure.Stage == ApplicationUpdateStage.ComponentPreparation &&
            failure.Error.Details!.Value.GetProperty("exceptionMessage").GetString()!.Contains(
                expectedMessage,
                StringComparison.OrdinalIgnoreCase));
        host.RuntimeAccess.Ports.ShouldBeNull();
    }

    private static async Task<IReadOnlyList<string>> ReceiveEventsThroughAsync(
        ISourceBlock<FlowMessage<ComponentEvent>> messages,
        string terminalEvent)
    {
        var names = new List<string>();
        while (!names.Contains(terminalEvent, StringComparer.Ordinal))
        {
            var message = await messages.ReceiveAsync().WaitAsync(Timeout);
            names.Add(message.Value.Name);
        }

        return names;
    }

    private static async Task<ComponentEvent> ReceiveEventAsync(
        ISourceBlock<FlowMessage<ComponentEvent>> messages,
        string eventName)
    {
        while (true)
        {
            var @event = (await messages.ReceiveAsync().WaitAsync(Timeout)).Value;
            if (string.Equals(@event.Name, eventName, StringComparison.Ordinal))
                return @event;
        }
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
