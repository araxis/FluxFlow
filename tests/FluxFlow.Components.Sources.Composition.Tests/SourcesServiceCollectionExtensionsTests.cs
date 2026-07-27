using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.Sources.Composition;
using FluxFlow.Components.Sources.Contracts;
using FluxFlow.Components.Sources.Nodes;
using FluxFlow.Composition;
using FluxFlow.Composition.Addressing;
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
            SourcesComponentPortNames.Output);
    private static readonly ApplicationAddress Events =
        ApplicationAddress.WorkflowPort(
            "main",
            "source",
            ComponentEvents.PortName);

    [Fact]
    public void AddSourcesComponents_exposes_canonical_typed_metadata()
    {
        var registry = ComponentCatalogTestHost.Create(
            services => services.AddSourcesComponents());

        registry.Components[SourcesComponentTypes.Generated]
            .Outputs[SourcesComponentPortNames.Output].MessageType.ShouldBe(
                typeof(JsonElement));
        registry.Components[SourcesComponentTypes.Sequence]
            .Outputs[SourcesComponentPortNames.Output].MessageType.ShouldBe(
                typeof(SequenceItem));
        typeof(SourcesServiceCollectionExtensions).GetMethods()
            .ShouldNotContain(static method => method.IsGenericMethodDefinition);
    }

    [Fact]
    public void AddSourcesComponents_is_idempotent()
    {
        var catalog = ComponentCatalogTestHost.Create(services =>
        {
            services.AddSourcesComponents();
            services.AddSourcesComponents();
        });

        catalog.Components.Keys.ShouldBe([
            SourcesComponentTypes.Generated,
            SourcesComponentTypes.Sequence
        ]);
    }

    [Fact]
    public void AddSourcesComponents_preserves_the_explicit_migration_alias()
    {
        var registry = ComponentCatalogTestHost.Create(
            services => services.AddSourcesComponents());

        registry.TryResolveType(
            SourcesComponentTypes.LegacyGenerated,
            out var canonicalType).ShouldBeTrue();
        canonicalType.ShouldBe(SourcesComponentTypes.Generated);
    }

    [Fact]
    public void Design_metadata_provider_returns_valid_source_metadata()
    {
        var metadata = new SourcesComponentDesignMetadataProvider().GetMetadata();

        metadata.Select(item => item.Type.Value).ShouldBe([
            SourcesComponentTypes.Generated,
            SourcesComponentTypes.Sequence
        ]);
        metadata.SelectMany(ComponentDesignMetadataValidator.Validate).ShouldBeEmpty();
        metadata.SelectMany(item => item.Options)
            .Select(option => option.Name.Value)
            .ShouldNotContain(SourcesComponentResourceNames.Clock);
        foreach (var item in metadata)
            AssertClockResource(item);

        metadata.Single(item =>
                item.Type.Value == SourcesComponentTypes.Generated)
            .Attributes.ContainsKey(new ComponentAttributeName("omittedOptions"))
            .ShouldBeFalse();
    }

    [Fact]
    public void Design_metadata_provider_describes_source_ports()
    {
        var metadata = MetadataByType();

        AssertSourcePorts(metadata[SourcesComponentTypes.Generated], nameof(JsonElement));
        AssertSourcePorts(metadata[SourcesComponentTypes.Sequence], nameof(SequenceItem));
    }

    [Fact]
    public void Design_metadata_provider_describes_source_options()
    {
        var metadata = MetadataByType();

        AssertOptions(
            metadata[SourcesComponentTypes.Generated],
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
            metadata[SourcesComponentTypes.Sequence],
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
        var generated = OptionsByName(metadata[SourcesComponentTypes.Generated]);
        var sequence = OptionsByName(metadata[SourcesComponentTypes.Sequence]);

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
        var catalog = ComponentCatalogTestHost.CreateDesignMetadataCatalog(
            static services => services.AddSourcesComponents());

        catalog.All.Count.ShouldBe(2);
        catalog.TryGet(
            new ComponentType(SourcesComponentTypes.Generated),
            out var generated).ShouldBeTrue();
        generated.ShouldNotBeNull();
        generated.Type.ShouldBe(new ComponentType(SourcesComponentTypes.Generated));
    }

    [Fact]
    public async Task Hosted_generated_source_binds_items_and_emits_events()
    {
        var clock = NewClock();
        var scheduled = clock.TimerScheduled;
        await using var host = await StartHostAsync(
            SourcesComponentTypes.Generated,
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
    public async Task Hosted_generated_source_normalizes_one_scalar_item()
    {
        var clock = NewClock();
        var scheduled = clock.TimerScheduled;
        await using var host = await StartHostAsync(
            SourcesComponentTypes.Generated,
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
            SourcesComponentTypes.Generated,
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
            SourcesComponentTypes.Sequence,
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
    [InlineData(SourcesComponentTypes.Generated, "boundedCapacity", 0, "capacity")]
    [InlineData(SourcesComponentTypes.Generated, "initialDelayMilliseconds", -1, "initialDelayMilliseconds")]
    [InlineData(SourcesComponentTypes.Generated, "intervalMilliseconds", -1, "intervalMilliseconds")]
    [InlineData(SourcesComponentTypes.Generated, "maxItems", 0, "maxItems")]
    [InlineData(SourcesComponentTypes.Generated, "loop", true, "maxItems")]
    [InlineData(SourcesComponentTypes.Sequence, "boundedCapacity", 0, "boundedCapacity")]
    [InlineData(SourcesComponentTypes.Sequence, "initialDelayMilliseconds", -1, "initialDelayMilliseconds")]
    [InlineData(SourcesComponentTypes.Sequence, "intervalMilliseconds", -1, "intervalMilliseconds")]
    [InlineData(SourcesComponentTypes.Sequence, "count", 0, "count")]
    [InlineData(SourcesComponentTypes.Sequence, "step", 0L, "step")]
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
        if (componentType == SourcesComponentTypes.Generated)
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

    private static void AssertSourcePorts(ComponentDesignMetadata metadata, string valueType)
    {
        metadata.Ports.Select(port => (
            port.Name.Value,
            port.Direction,
            port.Order,
            port.IsPrimary,
            port.ValueType?.Value)).ShouldBe([
            (SourcesComponentPortNames.Output, PortDirection.Output, 0, true, valueType)
        ]);
    }

    private static void AssertClockResource(ComponentDesignMetadata metadata)
    {
        var resource = metadata.Resources.ShouldHaveSingleItem();
        resource.Name.Value.ShouldBe(SourcesComponentResourceNames.Clock);
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
            componentProperties[SourcesComponentResourceNames.Clock] = "Resources.fixed";
            resources = ["fixed"];
        }

        return CanonicalApplicationTestHost.StartAsync(
            SingleComponent(
                componentType,
                componentProperties,
                resources,
                componentName: "source"),
            AddSourcesComponents,
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

    private static void AddSourcesComponents(IServiceCollection services)
        => services.AddSourcesComponents();

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
