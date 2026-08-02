using System.Text.Json;
using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.Timers.Contracts;
using FluxFlow.Components.Timers.Nodes;
using FluxFlow.Components.Timers.Options;
using FluxFlow.Composition;

namespace FluxFlow.Components.Timers.Composition;

public static class TimersServiceCollectionExtensions
{
    public static FluxFlowRegistrationBuilder AddTimers(this FluxFlowRegistrationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder
            .AddComponent(TimersComponentDefinition.Types.Interval, ConfigureInterval)
            .AddComponent(TimersComponentDefinition.Types.Schedule, ConfigureSchedule)
            .AddComponent(TimersComponentDefinition.Types.Delay, ConfigureDelay)
            .AddComponent(TimersComponentDefinition.Types.Throttle, ConfigureThrottle)
            .AddComponent(TimersComponentDefinition.Types.Debounce, ConfigureDebounce);
    }

    private static void ConfigureInterval(ComponentRegistrationBuilder component)
    {
        ConfigureCommon(component, CreateTimerIntervalNode, "Interval Timer", "Emits typed timer ticks on a fixed interval.", "timer", TimersComponentDefinition.Options.Interval);
        AddName(component, TimersComponentDefinition.Options.Interval);
        component.AddOption<TimeSpan>(TimersComponentDefinition.Options.Interval, OptionValueKind.Duration, "Interval", "Delay between ticks.", true, section: "Timing", importance: OptionDesignMetadataAttributeValues.Primary);
        component.AddOption<TimeSpan>(TimersComponentDefinition.Options.InitialDelay, OptionValueKind.Duration, "Initial Delay", "Optional delay before the first scheduled tick.", defaultValue: TimeSpan.Zero, section: "Timing", importance: OptionDesignMetadataAttributeValues.Advanced);
        component.AddOption<bool>(TimersComponentDefinition.Options.EmitImmediately, OptionValueKind.Boolean, "Emit Immediately", "Emit the first tick immediately when the source starts.", defaultValue: false, section: "Timing", importance: OptionDesignMetadataAttributeValues.Advanced);
        AddMaxTicks(component);
        AddCapacity(component);
        component.AddOutput<TimerIntervalTick>(TimersComponentDefinition.Ports.Output, "Output", "Messages", 1, "Interval timer tick.", true);
    }

    private static void ConfigureSchedule(ComponentRegistrationBuilder component)
    {
        ConfigureCommon(component, CreateTimerScheduleNode, "Schedule Timer", "Emits typed timer ticks from a cron schedule.", "calendar-clock", "schedule");
        component.AddAttribute("omittedOptions", "timeZone");
        component.AddAttribute("omittedOptionsReason", "TimerScheduleSettings.TimeZone requires typed configuration; this adapter does not add time-zone id conversion.");
        AddName(component, "schedule");
        component.AddOption<string>(TimersComponentDefinition.Options.Cron, OptionValueKind.Text, "Cron", "Five- or six-field cron expression. Schedule composition uses UTC unless the host provides a typed time-zone setting.", true, section: "Schedule", importance: OptionDesignMetadataAttributeValues.Primary, editor: OptionDesignMetadataAttributeValues.Text);
        AddMaxTicks(component);
        AddCapacity(component);
        component.AddOutput<TimerScheduleTick>(TimersComponentDefinition.Ports.Output, "Output", "Messages", 1, "Scheduled timer tick.", true);
    }

    private static void ConfigureDelay(ComponentRegistrationBuilder component)
    {
        ConfigureCommon(component, CreateTimerDelayNode, "Delay", "Emits a result for each workflow value after a configured delay.", TimersComponentDefinition.Resources.Clock, TimersComponentDefinition.Options.Delay);
        AddName(component, TimersComponentDefinition.Options.Delay);
        component.AddOption<TimeSpan>(TimersComponentDefinition.Options.Delay, OptionValueKind.Duration, "Delay", "Delay applied to each input message.", true, section: "Timing", importance: OptionDesignMetadataAttributeValues.Primary);
        AddCapacity(component);
        AddTransformPorts(component);
    }

    private static void ConfigureThrottle(ComponentRegistrationBuilder component)
    {
        ConfigureCommon(component, CreateTimerThrottleNode, "Throttle", "Rate-limits workflow values and emits ordered results.", "gauge", "throttle");
        AddName(component, "throttle");
        component.AddOption<TimeSpan>(TimersComponentDefinition.Options.Interval, OptionValueKind.Duration, "Interval", "Minimum delay between emitted messages.", true, section: "Timing", importance: OptionDesignMetadataAttributeValues.Primary);
        component.AddOption<bool>(TimersComponentDefinition.Options.EmitFirstImmediately, OptionValueKind.Boolean, "Emit First Immediately", "Emit the first input immediately before applying the throttle interval.", defaultValue: true, section: "Timing", importance: OptionDesignMetadataAttributeValues.Advanced);
        AddCapacity(component);
        AddTransformPorts(component);
    }

    private static void ConfigureDebounce(ComponentRegistrationBuilder component)
    {
        ConfigureCommon(component, CreateTimerDebounceNode, "Debounce", "Emits a result for the latest workflow value after a quiet period.", "timer-reset", "debounce");
        AddName(component, "debounce");
        component.AddOption<TimeSpan>(TimersComponentDefinition.Options.QuietPeriod, OptionValueKind.Duration, "Quiet Period", "Required quiet period before the latest input is emitted.", true, section: "Timing", importance: OptionDesignMetadataAttributeValues.Primary);
        AddCapacity(component);
        AddTransformPorts(component);
    }

    private static void ConfigureCommon(ComponentRegistrationBuilder component, ComponentFactory factory, string displayName, string summary, string iconKey, string preferredNodeName)
    {
        component.UseFactory(factory);
        component.WithDisplay(displayName, "Timers", summary, iconKey, preferredNodeName, 420);
        component.AddResource<TimeProvider>(TimersComponentDefinition.Resources.Clock, "Clock", 0, "Optional keyed clock for deterministic timer scheduling and diagnostics.", designValueType: nameof(TimeProvider), ownership: ResourceDesignMetadataAttributeValues.HostOwned, pickerKind: ResourceDesignMetadataAttributeValues.Clock, keyPattern: "Resources.{name}");
    }

    private static void AddName(ComponentRegistrationBuilder component, string defaultValue)
        => component.AddOption<string>(TimersComponentDefinition.Options.Name, OptionValueKind.Text, "Name", "Name emitted in timer diagnostics and payloads.", defaultValue: defaultValue, section: "Diagnostics", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Text);

    private static void AddMaxTicks(ComponentRegistrationBuilder component)
        => component.AddOption<long?>(TimersComponentDefinition.Options.MaxTicks, OptionValueKind.Number, "Max Ticks", "Optional maximum number of ticks to emit before completing.", min: 1, section: "Runtime", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Number);

    private static void AddCapacity(ComponentRegistrationBuilder component)
        => component.AddOption<int>(TimersComponentDefinition.Options.BoundedCapacity, OptionValueKind.Number, "Bounded Capacity", "Capacity used for bounded timer work and reliable normal-data output.", defaultValue: 128, min: 1, section: "Runtime", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Number);

    private static void AddTransformPorts(ComponentRegistrationBuilder component)
    {
        component.AddInput<JsonElement>(TimersComponentDefinition.Ports.Input, "Input", "Messages", 0, "Schema-less JSON value.", true);
        component.AddOutput<JsonElement>(TimersComponentDefinition.Ports.Output, "Output", "Messages", 1, "Delayed or rate-limited JSON value; failures use the message error case.", true);
    }

    private static ValueTask<ComponentInstance> CreateTimerIntervalNode(
        ComponentActivationContext context)
    {
        var settings = context.BindConfiguration<TimerIntervalSettings>();
        var clock = context.GetResource<TimeProvider>(
            TimersComponentDefinition.Resources.Clock);
        var node = new TimerIntervalNode(settings, clock);

        return ValueTask.FromResult(ComponentInstance.Create(
            node,
            outputs:
            [
                ComponentPorts.Output<TimerIntervalTick>(
                    TimersComponentDefinition.Ports.Output,
                    node.Output)
            ],
            events: node.Events));
    }

    private static ValueTask<ComponentInstance> CreateTimerScheduleNode(
        ComponentActivationContext context)
    {
        var settings = context.BindConfiguration<TimerScheduleSettings>();
        var clock = context.GetResource<TimeProvider>(
            TimersComponentDefinition.Resources.Clock);
        var node = new TimerScheduleNode(settings, clock);

        return ValueTask.FromResult(ComponentInstance.Create(
            node,
            outputs:
            [
                ComponentPorts.Output<TimerScheduleTick>(
                    TimersComponentDefinition.Ports.Output,
                    node.Output)
            ],
            events: node.Events));
    }

    private static ValueTask<ComponentInstance> CreateTimerDelayNode(
        ComponentActivationContext context)
    {
        var settings = context.BindConfiguration<TimerDelaySettings>();
        var clock = context.GetResource<TimeProvider>(
            TimersComponentDefinition.Resources.Clock);
        var node = new TimerDelayNode(settings, clock);

        return ValueTask.FromResult(ComponentInstance.Create(
            node,
            inputs:
            [
                ComponentPorts.Input<JsonElement>(
                    TimersComponentDefinition.Ports.Input,
                    node.Input)
            ],
            outputs:
            [
                ComponentPorts.Output<JsonElement>(
                    TimersComponentDefinition.Ports.Output,
                    node.Output)
            ],
            events: node.Events));
    }

    private static ValueTask<ComponentInstance> CreateTimerThrottleNode(
        ComponentActivationContext context)
    {
        var settings = context.BindConfiguration<TimerThrottleSettings>();
        var clock = context.GetResource<TimeProvider>(
            TimersComponentDefinition.Resources.Clock);
        var node = new TimerThrottleNode(settings, clock);

        return ValueTask.FromResult(ComponentInstance.Create(
            node,
            inputs:
            [
                ComponentPorts.Input<JsonElement>(
                    TimersComponentDefinition.Ports.Input,
                    node.Input)
            ],
            outputs:
            [
                ComponentPorts.Output<JsonElement>(
                    TimersComponentDefinition.Ports.Output,
                    node.Output)
            ],
            events: node.Events));
    }

    private static ValueTask<ComponentInstance> CreateTimerDebounceNode(
        ComponentActivationContext context)
    {
        var settings = context.BindConfiguration<TimerDebounceSettings>();
        var clock = context.GetResource<TimeProvider>(
            TimersComponentDefinition.Resources.Clock);
        var node = new TimerDebounceNode(settings, clock);

        return ValueTask.FromResult(ComponentInstance.Create(
            node,
            inputs:
            [
                ComponentPorts.Input<JsonElement>(
                    TimersComponentDefinition.Ports.Input,
                    node.Input)
            ],
            outputs:
            [
                ComponentPorts.Output<JsonElement>(
                    TimersComponentDefinition.Ports.Output,
                    node.Output)
            ],
            events: node.Events));
    }

}
