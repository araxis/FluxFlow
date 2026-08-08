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
            .AddDesignedComponent(TimersComponents.IntervalTimer)
            .AddDesignedComponent(TimersComponents.ScheduleTimer)
            .AddDesignedComponent(TimersComponents.Delay)
            .AddDesignedComponent(TimersComponents.Throttle)
            .AddDesignedComponent(TimersComponents.Debounce);
    }

    internal static void ConfigureInterval(ComponentRegistrationBuilder component)
    {
        ConfigureCommon(component, "Interval Timer", "Emits typed timer ticks on a fixed interval.", "timer", TimersComponentDefinition.Options.Interval);
        AddName(component, TimersComponentDefinition.Options.Interval);
        component.AddOption<TimeSpan>(TimersComponentDefinition.Options.Interval, OptionValueKind.Duration, "Interval", "Delay between ticks.", true, section: "Timing", importance: OptionDesignMetadataAttributeValues.Primary);
        component.AddOption<TimeSpan>(TimersComponentDefinition.Options.InitialDelay, OptionValueKind.Duration, "Initial Delay", "Optional delay before the first scheduled tick.", defaultValue: TimeSpan.Zero, section: "Timing", importance: OptionDesignMetadataAttributeValues.Advanced);
        component.AddOption<bool>(TimersComponentDefinition.Options.EmitImmediately, OptionValueKind.Boolean, "Emit Immediately", "Emit the first tick immediately when the source starts.", defaultValue: false, section: "Timing", importance: OptionDesignMetadataAttributeValues.Advanced);
        AddMaxTicks(component);
        AddCapacity(component);
        component
            .UseFactory(CreateTimerIntervalNode)
            .HasOutput(TimersComponentDefinition.Ports.Output, static node => node.Output, "Output", "Messages", 1, "Interval timer tick.", true)
            .HasEvents(TimersComponentDefinition.Ports.Events, static node => node.Events, "Events", "Diagnostics", 2, "Best-effort interval-timer diagnostics.");
    }

    internal static void ConfigureSchedule(ComponentRegistrationBuilder component)
    {
        ConfigureCommon(component, "Schedule Timer", "Emits typed timer ticks from a cron schedule.", "calendar-clock", "schedule");
        component.AddAttribute("omittedOptions", "timeZone");
        component.AddAttribute("omittedOptionsReason", "TimerScheduleSettings.TimeZone requires typed configuration; this adapter does not add time-zone id conversion.");
        AddName(component, "schedule");
        component.AddOption<string>(TimersComponentDefinition.Options.Cron, OptionValueKind.Text, "Cron", "Five- or six-field cron expression. Schedule composition uses UTC unless the host provides a typed time-zone setting.", true, section: "Schedule", importance: OptionDesignMetadataAttributeValues.Primary, editor: OptionDesignMetadataAttributeValues.Text);
        AddMaxTicks(component);
        AddCapacity(component);
        component
            .UseFactory(CreateTimerScheduleNode)
            .HasOutput(TimersComponentDefinition.Ports.Output, static node => node.Output, "Output", "Messages", 1, "Scheduled timer tick.", true)
            .HasEvents(TimersComponentDefinition.Ports.Events, static node => node.Events, "Events", "Diagnostics", 2, "Best-effort schedule-timer diagnostics.");
    }

    internal static void ConfigureDelay(ComponentRegistrationBuilder component)
    {
        ConfigureCommon(component, "Delay", "Emits a result for each workflow value after a configured delay.", TimersComponentDefinition.Resources.Clock, TimersComponentDefinition.Options.Delay);
        AddName(component, TimersComponentDefinition.Options.Delay);
        component.AddOption<TimeSpan>(TimersComponentDefinition.Options.Delay, OptionValueKind.Duration, "Delay", "Delay applied to each input message.", true, section: "Timing", importance: OptionDesignMetadataAttributeValues.Primary);
        AddCapacity(component);
        component
            .UseFactory(CreateTimerDelayNode)
            .HasInput(TimersComponentDefinition.Ports.Input, static node => node.Input, "Input", "Messages", 0, "Schema-less JSON value.", true)
            .HasOutput(TimersComponentDefinition.Ports.Output, static node => node.Output, "Output", "Messages", 1, "Delayed JSON value; failures use the message error case.", true)
            .HasEvents(TimersComponentDefinition.Ports.Events, static node => node.Events, "Events", "Diagnostics", 2, "Best-effort delay diagnostics.");
    }

    internal static void ConfigureThrottle(ComponentRegistrationBuilder component)
    {
        ConfigureCommon(component, "Throttle", "Rate-limits workflow values and emits ordered results.", "gauge", "throttle");
        AddName(component, "throttle");
        component.AddOption<TimeSpan>(TimersComponentDefinition.Options.Interval, OptionValueKind.Duration, "Interval", "Minimum delay between emitted messages.", true, section: "Timing", importance: OptionDesignMetadataAttributeValues.Primary);
        component.AddOption<bool>(TimersComponentDefinition.Options.EmitFirstImmediately, OptionValueKind.Boolean, "Emit First Immediately", "Emit the first input immediately before applying the throttle interval.", defaultValue: true, section: "Timing", importance: OptionDesignMetadataAttributeValues.Advanced);
        AddCapacity(component);
        component
            .UseFactory(CreateTimerThrottleNode)
            .HasInput(TimersComponentDefinition.Ports.Input, static node => node.Input, "Input", "Messages", 0, "Schema-less JSON value.", true)
            .HasOutput(TimersComponentDefinition.Ports.Output, static node => node.Output, "Output", "Messages", 1, "Rate-limited JSON value; failures use the message error case.", true)
            .HasEvents(TimersComponentDefinition.Ports.Events, static node => node.Events, "Events", "Diagnostics", 2, "Best-effort throttle diagnostics.");
    }

    internal static void ConfigureDebounce(ComponentRegistrationBuilder component)
    {
        ConfigureCommon(component, "Debounce", "Emits a result for the latest workflow value after a quiet period.", "timer-reset", "debounce");
        AddName(component, "debounce");
        component.AddOption<TimeSpan>(TimersComponentDefinition.Options.QuietPeriod, OptionValueKind.Duration, "Quiet Period", "Required quiet period before the latest input is emitted.", true, section: "Timing", importance: OptionDesignMetadataAttributeValues.Primary);
        AddCapacity(component);
        component
            .UseFactory(CreateTimerDebounceNode)
            .HasInput(TimersComponentDefinition.Ports.Input, static node => node.Input, "Input", "Messages", 0, "Schema-less JSON value.", true)
            .HasOutput(TimersComponentDefinition.Ports.Output, static node => node.Output, "Output", "Messages", 1, "Debounced JSON value; failures use the message error case.", true)
            .HasEvents(TimersComponentDefinition.Ports.Events, static node => node.Events, "Events", "Diagnostics", 2, "Best-effort debounce diagnostics.");
    }

    private static void ConfigureCommon(ComponentRegistrationBuilder component, string displayName, string summary, string iconKey, string preferredNodeName)
    {
        component.WithDisplay(displayName, "Timers", summary, iconKey, preferredNodeName, 420);
        component.AddResource<TimeProvider>(TimersComponentDefinition.Resources.Clock, "Clock", 0, "Optional keyed clock for deterministic timer scheduling and diagnostics.", designValueType: nameof(TimeProvider), ownership: ResourceDesignMetadataAttributeValues.HostOwned, pickerKind: ResourceDesignMetadataAttributeValues.Clock, keyPattern: "Resources.{name}");
    }

    private static void AddName(ComponentRegistrationBuilder component, string defaultValue)
        => component.AddOption<string>(TimersComponentDefinition.Options.Name, OptionValueKind.Text, "Name", "Name emitted in timer diagnostics and payloads.", defaultValue: defaultValue, section: "Diagnostics", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Text);

    private static void AddMaxTicks(ComponentRegistrationBuilder component)
        => component.AddOption<long?>(TimersComponentDefinition.Options.MaxTicks, OptionValueKind.Number, "Max Ticks", "Optional maximum number of ticks to emit before completing.", min: 1, section: "Runtime", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Number);

    private static void AddCapacity(ComponentRegistrationBuilder component)
        => component.AddOption<int>(TimersComponentDefinition.Options.BoundedCapacity, OptionValueKind.Number, "Bounded Capacity", "Capacity used for bounded timer work and reliable normal-data output.", defaultValue: 128, min: 1, section: "Runtime", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Number);

    private static TimerIntervalNode CreateTimerIntervalNode(
        ComponentActivationContext context)
    {
        var settings = context.BindConfiguration<TimerIntervalSettings>();
        var clock = context.GetResource<TimeProvider>(
            TimersComponentDefinition.Resources.Clock);
        return new TimerIntervalNode(settings, clock);
    }

    private static TimerScheduleNode CreateTimerScheduleNode(
        ComponentActivationContext context)
    {
        var settings = context.BindConfiguration<TimerScheduleSettings>();
        var clock = context.GetResource<TimeProvider>(
            TimersComponentDefinition.Resources.Clock);
        return new TimerScheduleNode(settings, clock);
    }

    private static TimerDelayNode CreateTimerDelayNode(
        ComponentActivationContext context)
    {
        var settings = context.BindConfiguration<TimerDelaySettings>();
        var clock = context.GetResource<TimeProvider>(
            TimersComponentDefinition.Resources.Clock);
        return new TimerDelayNode(settings, clock);
    }

    private static TimerThrottleNode CreateTimerThrottleNode(
        ComponentActivationContext context)
    {
        var settings = context.BindConfiguration<TimerThrottleSettings>();
        var clock = context.GetResource<TimeProvider>(
            TimersComponentDefinition.Resources.Clock);
        return new TimerThrottleNode(settings, clock);
    }

    private static TimerDebounceNode CreateTimerDebounceNode(
        ComponentActivationContext context)
    {
        var settings = context.BindConfiguration<TimerDebounceSettings>();
        var clock = context.GetResource<TimeProvider>(
            TimersComponentDefinition.Resources.Clock);
        return new TimerDebounceNode(settings, clock);
    }

}
