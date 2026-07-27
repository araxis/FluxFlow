using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.Timers.Composition;
using FluxFlow.Components.Timers.Contracts;
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

namespace FluxFlow.Components.Timers.Composition.Tests;

public sealed class TimersServiceCollectionExtensionsTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private static readonly ApplicationAddress Input =
        ApplicationAddress.WorkflowPort("main", "timer", TimersComponentDefinition.Ports.Input);
    private static readonly ApplicationAddress Output =
        ApplicationAddress.WorkflowPort("main", "timer", TimersComponentDefinition.Ports.Output);

    [Fact]
    public void AddTimersComponents_registers_timer_metadata()
    {
        var registry = ComponentCatalogTestHost.Create(
            services => services.AddTimersComponents());

        registry.Components[TimersComponentDefinition.Types.Interval]
            .Outputs[TimersComponentDefinition.Ports.Output].MessageType.ShouldBe(
                typeof(TimerIntervalTick));
        registry.Components[TimersComponentDefinition.Types.Schedule]
            .Outputs[TimersComponentDefinition.Ports.Output].MessageType.ShouldBe(
                typeof(TimerScheduleTick));

        AssertTransformMetadata(registry, TimersComponentDefinition.Types.Delay);
        AssertTransformMetadata(registry, TimersComponentDefinition.Types.Throttle);
        AssertTransformMetadata(registry, TimersComponentDefinition.Types.Debounce);

        typeof(TimersServiceCollectionExtensions).GetMethods()
            .ShouldNotContain(static method => method.IsGenericMethodDefinition);
    }

    [Fact]
    public void AddTimersComponents_is_idempotent()
    {
        var catalog = ComponentCatalogTestHost.Create(services =>
        {
            services.AddTimersComponents();
            services.AddTimersComponents();
        });

        catalog.Components.Keys.ShouldBe([
            TimersComponentDefinition.Types.Debounce,
            TimersComponentDefinition.Types.Delay,
            TimersComponentDefinition.Types.Interval,
            TimersComponentDefinition.Types.Schedule,
            TimersComponentDefinition.Types.Throttle
        ]);
    }

    [Fact]
    public void Design_metadata_provider_returns_valid_timer_metadata()
    {
        var metadata = TimersComponentDefinition.CreateMetadata();

        metadata.Select(item => item.Type.Value).ShouldBe([
            TimersComponentDefinition.Types.Interval,
            TimersComponentDefinition.Types.Schedule,
            TimersComponentDefinition.Types.Delay,
            TimersComponentDefinition.Types.Throttle,
            TimersComponentDefinition.Types.Debounce
        ]);
        metadata.SelectMany(ComponentDesignMetadataValidator.Validate).ShouldBeEmpty();
        metadata.SelectMany(item => item.Options)
            .Select(option => option.Name.Value)
            .ShouldNotContain(TimersComponentDefinition.Resources.Clock);
        foreach (var item in metadata)
        {
            AssertClockResource(item);
        }
    }

    [Fact]
    public void Design_metadata_provider_describes_timer_ports()
    {
        var metadata = MetadataByType();

        AssertSourcePorts(metadata[TimersComponentDefinition.Types.Interval], nameof(TimerIntervalTick));
        AssertSourcePorts(metadata[TimersComponentDefinition.Types.Schedule], nameof(TimerScheduleTick));
        AssertTransformPorts(metadata[TimersComponentDefinition.Types.Delay]);
        AssertTransformPorts(metadata[TimersComponentDefinition.Types.Throttle]);
        AssertTransformPorts(metadata[TimersComponentDefinition.Types.Debounce]);
    }

    [Fact]
    public void Design_metadata_provider_describes_timer_options()
    {
        var metadata = MetadataByType();

        AssertOptions(
            metadata[TimersComponentDefinition.Types.Interval],
            [
                ("name", OptionValueKind.Text, "interval", false),
                ("interval", OptionValueKind.Duration, null, true),
                ("initialDelay", OptionValueKind.Duration, TimeSpan.Zero, false),
                ("emitImmediately", OptionValueKind.Boolean, false, false),
                ("maxTicks", OptionValueKind.Number, null, false),
                ("boundedCapacity", OptionValueKind.Number, 128, false)
            ]);
        AssertOptions(
            metadata[TimersComponentDefinition.Types.Schedule],
            [
                ("name", OptionValueKind.Text, "schedule", false),
                ("cron", OptionValueKind.Text, null, true),
                ("maxTicks", OptionValueKind.Number, null, false),
                ("boundedCapacity", OptionValueKind.Number, 128, false)
            ]);
        metadata[TimersComponentDefinition.Types.Schedule].Options
            .Select(option => option.Name.Value)
            .ShouldNotContain("timeZone");
        metadata[TimersComponentDefinition.Types.Schedule]
            .Attributes[new ComponentAttributeName("omittedOptions")]
            .Value.ShouldBe("timeZone");
        AssertOptions(
            metadata[TimersComponentDefinition.Types.Delay],
            [
                ("name", OptionValueKind.Text, "delay", false),
                ("delay", OptionValueKind.Duration, null, true),
                ("boundedCapacity", OptionValueKind.Number, 128, false)
            ]);
        AssertOptions(
            metadata[TimersComponentDefinition.Types.Throttle],
            [
                ("name", OptionValueKind.Text, "throttle", false),
                ("interval", OptionValueKind.Duration, null, true),
                ("emitFirstImmediately", OptionValueKind.Boolean, true, false),
                ("boundedCapacity", OptionValueKind.Number, 128, false)
            ]);
        AssertOptions(
            metadata[TimersComponentDefinition.Types.Debounce],
            [
                ("name", OptionValueKind.Text, "debounce", false),
                ("quietPeriod", OptionValueKind.Duration, null, true),
                ("boundedCapacity", OptionValueKind.Number, 128, false)
            ]);

        metadata.Values
            .SelectMany(item => item.Options)
            .Where(option => option.Name.Value == "boundedCapacity" || option.Name.Value == "maxTicks")
            .ShouldAllBe(option => option.Min == 1);
    }

    [Fact]
    public void Design_metadata_provider_describes_timer_option_hints()
    {
        var metadata = MetadataByType();

        var intervalOptions = OptionsByName(metadata[TimersComponentDefinition.Types.Interval]);
        AssertOptionHints(intervalOptions["name"], "Diagnostics", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Text);
        AssertOptionHints(intervalOptions["interval"], "Timing", OptionDesignMetadataAttributeValues.Primary);
        AssertOptionHints(intervalOptions["initialDelay"], "Timing", OptionDesignMetadataAttributeValues.Advanced);
        AssertOptionHints(intervalOptions["emitImmediately"], "Timing", OptionDesignMetadataAttributeValues.Advanced);
        AssertOptionHints(intervalOptions["maxTicks"], "Runtime", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Number);
        AssertOptionHints(intervalOptions["boundedCapacity"], "Runtime", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Number);

        var schedule = metadata[TimersComponentDefinition.Types.Schedule];
        var scheduleOptions = OptionsByName(schedule);
        AssertOptionHints(scheduleOptions["name"], "Diagnostics", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Text);
        AssertOptionHints(scheduleOptions["cron"], "Schedule", OptionDesignMetadataAttributeValues.Primary, OptionDesignMetadataAttributeValues.Text);
        AssertOptionHints(scheduleOptions["maxTicks"], "Runtime", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Number);
        AssertOptionHints(scheduleOptions["boundedCapacity"], "Runtime", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Number);
        AttributeValue(schedule.Attributes, "omittedOptions").ShouldBe("timeZone");
        AttributeValue(schedule.Attributes, "omittedOptionsReason")
            .ShouldBe("TimerScheduleSettings.TimeZone requires typed configuration; this adapter does not add time-zone id conversion.");

        var delayOptions = OptionsByName(metadata[TimersComponentDefinition.Types.Delay]);
        AssertOptionHints(delayOptions["name"], "Diagnostics", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Text);
        AssertOptionHints(delayOptions["delay"], "Timing", OptionDesignMetadataAttributeValues.Primary);
        AssertOptionHints(delayOptions["boundedCapacity"], "Runtime", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Number);

        var throttleOptions = OptionsByName(metadata[TimersComponentDefinition.Types.Throttle]);
        AssertOptionHints(throttleOptions["name"], "Diagnostics", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Text);
        AssertOptionHints(throttleOptions["interval"], "Timing", OptionDesignMetadataAttributeValues.Primary);
        AssertOptionHints(throttleOptions["emitFirstImmediately"], "Timing", OptionDesignMetadataAttributeValues.Advanced);
        AssertOptionHints(throttleOptions["boundedCapacity"], "Runtime", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Number);

        var debounceOptions = OptionsByName(metadata[TimersComponentDefinition.Types.Debounce]);
        AssertOptionHints(debounceOptions["name"], "Diagnostics", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Text);
        AssertOptionHints(debounceOptions["quietPeriod"], "Timing", OptionDesignMetadataAttributeValues.Primary);
        AssertOptionHints(debounceOptions["boundedCapacity"], "Runtime", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Number);
    }

    [Fact]
    public void Design_metadata_provider_describes_timer_resource_picker_hints()
    {
        var metadata = MetadataByType();

        foreach (var item in metadata.Values)
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
            static services => services.AddTimersComponents());

        catalog.All.Count.ShouldBe(5);
        catalog.TryGet(
            new ComponentType(TimersComponentDefinition.Types.Interval),
            out var interval).ShouldBeTrue();
        interval.ShouldNotBeNull();
        interval.Type.ShouldBe(new ComponentType(TimersComponentDefinition.Types.Interval));
    }

    [Fact]
    public async Task Hosted_interval_resolves_keyed_clock_and_emits_ticks()
    {
        var startedAt = new DateTimeOffset(2026, 6, 2, 12, 0, 0, TimeSpan.Zero);
        var clock = new TrackingFakeTimeProvider(startedAt);
        var firstScheduled = clock.TimerScheduled;
        await using var host = await StartHostAsync(
            TimersComponentDefinition.Types.Interval,
            Properties(
                ("name", "poll"),
                ("interval", TimeSpan.FromMilliseconds(10)),
                ("initialDelay", TimeSpan.FromMilliseconds(10)),
                ("maxTicks", 2),
                ("boundedCapacity", 8)),
            clock);
        host.StartResult.Succeeded.ShouldBeTrue();
        await firstScheduled.WaitAsync(Timeout);
        var ports = host.GetRequiredPorts();
        var observed = (await ports.ObserveAsync<TimerIntervalTick>(Output))
            .Observation.ShouldNotBeNull();
        await using var observation = observed;

        var secondScheduled = clock.TimerScheduled;
        clock.Advance(TimeSpan.FromMilliseconds(10));
        await secondScheduled.WaitAsync(Timeout);
        clock.Advance(TimeSpan.FromMilliseconds(10));

        var first = await observation.Messages.ReceiveAsync().WaitAsync(Timeout);
        var second = await observation.Messages.ReceiveAsync().WaitAsync(Timeout);

        first.Value.Name.ShouldBe("poll");
        first.Value.Timestamp
            .ShouldBe(startedAt.AddMilliseconds(10));
        second.Value.Sequence.ShouldBe(2);
        second.Value.Timestamp
            .ShouldBe(startedAt.AddMilliseconds(20));
        first.MessageId.ShouldNotBe(second.MessageId);
    }

    [Fact]
    public async Task Hosted_schedule_binds_cron_and_emits_tick()
    {
        var startedAt = new DateTimeOffset(2026, 6, 2, 11, 59, 59, TimeSpan.Zero);
        var clock = new TrackingFakeTimeProvider(startedAt);
        var scheduled = clock.TimerScheduled;
        await using var host = await StartHostAsync(
            TimersComponentDefinition.Types.Schedule,
            Properties(
                ("name", "cron"),
                ("cron", "* * * * * *"),
                ("maxTicks", 1),
                ("boundedCapacity", 8)),
            clock);
        host.StartResult.Succeeded.ShouldBeTrue();
        await scheduled.WaitAsync(Timeout);
        var observed = (await host.GetRequiredPorts().ObserveAsync<TimerScheduleTick>(Output))
            .Observation.ShouldNotBeNull();
        await using var observation = observed;

        clock.Advance(TimeSpan.FromSeconds(1));
        var tick = await observation.Messages.ReceiveAsync().WaitAsync(Timeout);

        var value = tick.Value;
        value.Name.ShouldBe("cron");
        value.Cron.ShouldBe("* * * * * *");
        value.TimeZoneId.ShouldBe(TimeZoneInfo.Utc.Id);
        value.DueAt.ShouldBe(startedAt.AddSeconds(1));
    }

    [Fact]
    public async Task Hosted_delay_preserves_correlation_and_binds_settings()
    {
        var clock = new TrackingFakeTimeProvider(
            new DateTimeOffset(2026, 6, 2, 12, 0, 0, TimeSpan.Zero));
        await using var host = await StartHostAsync(
            TimersComponentDefinition.Types.Delay,
            Properties(
                ("name", "hold"),
                ("delay", TimeSpan.FromMilliseconds(35)),
                ("boundedCapacity", 8)),
            clock);
        host.StartResult.Succeeded.ShouldBeTrue();
        var ports = host.GetRequiredPorts();
        var observed = (await ports.ObserveAsync<JsonElement>(Output))
            .Observation.ShouldNotBeNull();
        await using var observation = observed;
        var message = FlowMessage.Create(
            JsonSerializer.SerializeToElement("one"),
            new CorrelationId("delay-correlation"));
        var scheduled = clock.TimerScheduled;

        (await ports.SendAsync(Input, message)).IsAccepted.ShouldBeTrue();
        await scheduled.WaitAsync(Timeout);
        clock.Advance(TimeSpan.FromMilliseconds(35));
        var delayed = await observation.Messages.ReceiveAsync().WaitAsync(Timeout);

        delayed.IsError.ShouldBeFalse();
        delayed.Value.ShouldBe(message.Value);
        delayed.CorrelationId.ShouldBe(new CorrelationId("delay-correlation"));
    }

    [Fact]
    public async Task Hosted_throttle_preserves_correlation_and_binds_settings()
    {
        var clock = new TrackingFakeTimeProvider(
            new DateTimeOffset(2026, 6, 2, 12, 0, 0, TimeSpan.Zero));
        await using var host = await StartHostAsync(
            TimersComponentDefinition.Types.Throttle,
            Properties(
                ("name", "rate"),
                ("interval", TimeSpan.FromMilliseconds(40)),
                ("emitFirstImmediately", false),
                ("boundedCapacity", 8)),
            clock);
        host.StartResult.Succeeded.ShouldBeTrue();
        var ports = host.GetRequiredPorts();
        var observed = (await ports.ObserveAsync<JsonElement>(Output))
            .Observation.ShouldNotBeNull();
        await using var observation = observed;
        var message = FlowMessage.Create(
            JsonSerializer.SerializeToElement("one"),
            new CorrelationId("throttle-correlation"));
        var scheduled = clock.TimerScheduled;

        (await ports.SendAsync(Input, message)).IsAccepted.ShouldBeTrue();
        await scheduled.WaitAsync(Timeout);
        clock.Advance(TimeSpan.FromMilliseconds(40));
        var throttled = await observation.Messages.ReceiveAsync().WaitAsync(Timeout);

        throttled.IsError.ShouldBeFalse();
        throttled.Value.ShouldBe(message.Value);
        throttled.CorrelationId.ShouldBe(new CorrelationId("throttle-correlation"));
    }

    [Fact]
    public async Task Hosted_debounce_preserves_latest_correlation_and_binds_settings()
    {
        var clock = new TrackingFakeTimeProvider(
            new DateTimeOffset(2026, 6, 2, 12, 0, 0, TimeSpan.Zero));
        await using var host = await StartHostAsync(
            TimersComponentDefinition.Types.Debounce,
            Properties(
                ("name", "quiet"),
                ("quietPeriod", TimeSpan.FromMilliseconds(25)),
                ("boundedCapacity", 8)),
            clock);
        host.StartResult.Succeeded.ShouldBeTrue();
        var ports = host.GetRequiredPorts();
        var observed = (await ports.ObserveAsync<JsonElement>(Output))
            .Observation.ShouldNotBeNull();
        await using var observation = observed;
        var first = FlowMessage.Create(
            JsonSerializer.SerializeToElement("one"),
            new CorrelationId("debounce-old"));
        var latest = FlowMessage.Create(
            JsonSerializer.SerializeToElement("two"),
            new CorrelationId("debounce-latest"));

        var scheduled1 = clock.TimerScheduled;
        (await ports.SendAsync(Input, first)).IsAccepted.ShouldBeTrue();
        await scheduled1.WaitAsync(Timeout);
        var scheduled2 = clock.TimerScheduled;
        (await ports.SendAsync(Input, latest)).IsAccepted.ShouldBeTrue();
        await scheduled2.WaitAsync(Timeout);
        clock.Advance(TimeSpan.FromMilliseconds(25));
        var debounced = await observation.Messages.ReceiveAsync().WaitAsync(Timeout);

        debounced.IsError.ShouldBeFalse();
        debounced.Value.GetString().ShouldBe("two");
        debounced.CorrelationId.ShouldBe(new CorrelationId("debounce-latest"));
    }

    [Fact]
    public async Task Invalid_timer_configuration_surfaces_factory_diagnostic()
    {
        await using (var host = await StartHostAsync(
            TimersComponentDefinition.Types.Interval,
            Properties(("interval", TimeSpan.Zero))))
        {
            AssertPreparationFailure(host, "Interval");
        }

        await using (var host = await StartHostAsync(
            TimersComponentDefinition.Types.Delay,
            Properties(("delay", TimeSpan.FromMilliseconds(-1)))))
        {
            AssertPreparationFailure(host, "Delay");
        }

        await using (var host = await StartHostAsync(
            TimersComponentDefinition.Types.Throttle,
            Properties(
                ("interval", TimeSpan.FromMilliseconds(1)),
                ("boundedCapacity", 0))))
        {
            AssertPreparationFailure(host, "BoundedCapacity");
        }

        await using (var host = await StartHostAsync(
            TimersComponentDefinition.Types.Debounce,
            Properties(("quietPeriod", TimeSpan.Zero))))
        {
            AssertPreparationFailure(host, "QuietPeriod");
        }
    }

    private static void AssertTransformMetadata(
        ComponentCatalog registry,
        string nodeType)
    {
        registry.Components[nodeType]
            .Inputs[TimersComponentDefinition.Ports.Input].MessageType.ShouldBe(
                typeof(JsonElement));
        registry.Components[nodeType]
            .Outputs[TimersComponentDefinition.Ports.Output].MessageType.ShouldBe(
                typeof(JsonElement));
    }

    private static Dictionary<string, ComponentDesignMetadata> MetadataByType()
        => TimersComponentDefinition.CreateMetadata()
            .ToDictionary(item => item.Type.Value, StringComparer.Ordinal);

    private static Dictionary<string, OptionDesignMetadata> OptionsByName(
        ComponentDesignMetadata metadata)
        => metadata.Options.ToDictionary(
            option => option.Name.Value,
            StringComparer.Ordinal);

    private static void AssertSourcePorts(
        ComponentDesignMetadata metadata,
        string outputType)
    {
        metadata.Ports.Select(port => (
            port.Name.Value,
            port.Direction,
            port.Order,
            port.IsPrimary,
            port.ValueType?.Value)).ShouldBe([
            (TimersComponentDefinition.Ports.Output, PortDirection.Output, 1, true, outputType)
        ]);
    }

    private static void AssertTransformPorts(ComponentDesignMetadata metadata)
    {
        metadata.Ports.Select(port => (
            port.Name.Value,
            port.Direction,
            port.Order,
            port.IsPrimary,
            port.ValueType?.Value)).ShouldBe([
            (TimersComponentDefinition.Ports.Input, PortDirection.Input, 0, true, nameof(JsonElement)),
            (TimersComponentDefinition.Ports.Output, PortDirection.Output, 1, true, nameof(JsonElement))
        ]);
    }

    private static void AssertClockResource(ComponentDesignMetadata metadata)
    {
        var resource = metadata.Resources.ShouldHaveSingleItem();

        resource.Name.Value.ShouldBe(TimersComponentDefinition.Resources.Clock);
        resource.DisplayName?.Value.ShouldBe("Clock");
        resource.Order.ShouldBe(0);
        resource.IsRequired.ShouldBeFalse();
        resource.ValueType?.Value.ShouldBe(nameof(TimeProvider));
    }

    private static void AssertOptions(
        ComponentDesignMetadata metadata,
        IReadOnlyList<(string Name, OptionValueKind Kind, object? DefaultValue, bool IsRequired)> expected)
    {
        metadata.Options.Select(option => (
            option.Name.Value,
            option.Kind,
            option.DefaultValue,
            option.IsRequired)).ShouldBe(expected);
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
            option.Attributes.ContainsKey(new ComponentAttributeName(OptionDesignMetadataAttributeNames.Editor))
                .ShouldBeFalse();
        }
        else
        {
            AttributeValue(option.Attributes, OptionDesignMetadataAttributeNames.Editor)
                .ShouldBe(editor);
        }

        option.Attributes.ContainsKey(new ComponentAttributeName(OptionDesignMetadataAttributeNames.Syntax))
            .ShouldBeFalse();
        option.Attributes.ContainsKey(new ComponentAttributeName(OptionDesignMetadataAttributeNames.RelatedResource))
            .ShouldBeFalse();
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
            componentProperties[TimersComponentDefinition.Resources.Clock] = "Resources.fixed";
            resources = ["fixed"];
        }

        return CanonicalApplicationTestHost.StartAsync(
            SingleComponent(
                componentType,
                componentProperties,
                resources,
                componentName: "timer"),
            AddTimersComponents,
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

    private static void AddTimersComponents(IServiceCollection services)
        => services.AddTimersComponents();

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

    private sealed class TrackingFakeTimeProvider : FakeTimeProvider
    {
        private readonly object _gate = new();
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
                {
                    return _nextTimer.Task;
                }
            }
        }

        private static TaskCompletionSource CreateSource()
            => new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
