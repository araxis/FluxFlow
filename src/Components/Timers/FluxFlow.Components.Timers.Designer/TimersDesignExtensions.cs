using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.Timers.Composition;
using FluxFlow.Composition;

namespace FluxFlow.Components.Timers.Designer;

public static class TimersDesignExtensions
{
    public static FluxFlowRegistrationBuilder AddTimersDesign(this FluxFlowRegistrationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder
            .AddDesignMetadata(TimersComponentDefinition.Types.Interval, DescribeInterval)
            .AddDesignMetadata(TimersComponentDefinition.Types.Schedule, DescribeSchedule)
            .AddDesignMetadata(TimersComponentDefinition.Types.Delay, DescribeDelay)
            .AddDesignMetadata(TimersComponentDefinition.Types.Throttle, DescribeThrottle)
            .AddDesignMetadata(TimersComponentDefinition.Types.Debounce, DescribeDebounce);
    }

    private static ComponentDesignMetadata DescribeInterval(ComponentDesignMetadata metadata)
    {
        metadata = metadata.WithDisplay("Interval Timer", "Timers", "Emits typed timer ticks on a fixed interval.", "timer", TimersComponentDefinition.Options.Interval, 420);
        metadata = metadata.WithResource(TimersComponentDefinition.Resources.Clock, "Clock", 0, "Optional keyed clock for deterministic timer scheduling and diagnostics.", designValueType: nameof(TimeProvider), ownership: ResourceDesignMetadataAttributeValues.HostOwned, pickerKind: ResourceDesignMetadataAttributeValues.Clock, keyPattern: "Resources.{name}");
        metadata = metadata.WithOption(TimersComponentDefinition.Options.Interval, OptionValueKind.Duration, "Interval", "Delay between ticks.", section: "Timing", importance: OptionDesignMetadataAttributeValues.Primary);
        metadata = metadata.WithOption(TimersComponentDefinition.Options.InitialDelay, OptionValueKind.Duration, "Initial Delay", "Optional delay before the first scheduled tick.", defaultValue: TimeSpan.Zero, section: "Timing", importance: OptionDesignMetadataAttributeValues.Advanced);
        metadata = metadata.WithOption(TimersComponentDefinition.Options.EmitImmediately, OptionValueKind.Boolean, "Emit Immediately", "Emit the first tick immediately when the source starts.", defaultValue: false, section: "Timing", importance: OptionDesignMetadataAttributeValues.Advanced);
        metadata = metadata.WithOption(TimersComponentDefinition.Options.MaxTicks, OptionValueKind.Number, "Max Ticks", "Optional maximum number of ticks to emit before completing.", min: 1, section: "Runtime", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Number);
        metadata = metadata.WithOption(TimersComponentDefinition.Options.BoundedCapacity, OptionValueKind.Number, "Bounded Capacity", "Capacity used for bounded timer work and reliable normal-data output.", defaultValue: 128, min: 1, section: "Runtime", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Number);
        metadata = metadata
            .WithPort(TimersComponentDefinition.Ports.Output, PortDirection.Output, "Output", "Messages", 1, "Interval timer tick.", true)
            .WithPort(TimersComponentDefinition.Ports.Events, PortDirection.Output, "Events", "Diagnostics", 2, "Best-effort interval-timer diagnostics.");
        return metadata;
    }

    private static ComponentDesignMetadata DescribeSchedule(ComponentDesignMetadata metadata)
    {
        metadata = metadata.WithDisplay("Schedule Timer", "Timers", "Emits typed timer ticks from a cron schedule.", "calendar-clock", "schedule", 420);
        metadata = metadata.WithResource(TimersComponentDefinition.Resources.Clock, "Clock", 0, "Optional keyed clock for deterministic timer scheduling and diagnostics.", designValueType: nameof(TimeProvider), ownership: ResourceDesignMetadataAttributeValues.HostOwned, pickerKind: ResourceDesignMetadataAttributeValues.Clock, keyPattern: "Resources.{name}");
        metadata = metadata.WithAttribute("omittedOptions", "timeZone", appendExistingWith: ",");
        metadata = metadata.WithAttribute("omittedOptionsReason", "TimerScheduleSettings.TimeZone requires typed configuration; this adapter does not add time-zone id conversion.", appendExistingWith: " ");
        metadata = metadata.WithOption(TimersComponentDefinition.Options.Cron, OptionValueKind.Text, "Cron", "Five- or six-field cron expression. Schedule composition uses UTC unless the host provides a typed time-zone setting.", section: "Schedule", importance: OptionDesignMetadataAttributeValues.Primary, editor: OptionDesignMetadataAttributeValues.Text);
        metadata = metadata.WithOption(TimersComponentDefinition.Options.MaxTicks, OptionValueKind.Number, "Max Ticks", "Optional maximum number of ticks to emit before completing.", min: 1, section: "Runtime", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Number);
        metadata = metadata.WithOption(TimersComponentDefinition.Options.BoundedCapacity, OptionValueKind.Number, "Bounded Capacity", "Capacity used for bounded timer work and reliable normal-data output.", defaultValue: 128, min: 1, section: "Runtime", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Number);
        metadata = metadata
            .WithPort(TimersComponentDefinition.Ports.Output, PortDirection.Output, "Output", "Messages", 1, "Scheduled timer tick.", true)
            .WithPort(TimersComponentDefinition.Ports.Events, PortDirection.Output, "Events", "Diagnostics", 2, "Best-effort schedule-timer diagnostics.");
        return metadata;
    }

    private static ComponentDesignMetadata DescribeDelay(ComponentDesignMetadata metadata)
    {
        metadata = metadata.WithDisplay("Delay", "Timers", "Emits a result for each workflow value after a configured delay.", TimersComponentDefinition.Resources.Clock, TimersComponentDefinition.Options.Delay, 420);
        metadata = metadata.WithResource(TimersComponentDefinition.Resources.Clock, "Clock", 0, "Optional keyed clock for deterministic timer scheduling and diagnostics.", designValueType: nameof(TimeProvider), ownership: ResourceDesignMetadataAttributeValues.HostOwned, pickerKind: ResourceDesignMetadataAttributeValues.Clock, keyPattern: "Resources.{name}");
        metadata = metadata.WithOption(TimersComponentDefinition.Options.Delay, OptionValueKind.Duration, "Delay", "Delay applied to each input message.", section: "Timing", importance: OptionDesignMetadataAttributeValues.Primary);
        metadata = metadata.WithOption(TimersComponentDefinition.Options.BoundedCapacity, OptionValueKind.Number, "Bounded Capacity", "Capacity used for bounded timer work and reliable normal-data output.", defaultValue: 128, min: 1, section: "Runtime", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Number);
        metadata = metadata
            .WithPort(TimersComponentDefinition.Ports.Input, PortDirection.Input, "Input", "Messages", 0, "Schema-less JSON value.", true)
            .WithPort(TimersComponentDefinition.Ports.Output, PortDirection.Output, "Output", "Messages", 1, "Delayed JSON value; failures use the message error case.", true)
            .WithPort(TimersComponentDefinition.Ports.Events, PortDirection.Output, "Events", "Diagnostics", 2, "Best-effort delay diagnostics.");
        return metadata;
    }

    private static ComponentDesignMetadata DescribeThrottle(ComponentDesignMetadata metadata)
    {
        metadata = metadata.WithDisplay("Throttle", "Timers", "Rate-limits workflow values and emits ordered results.", "gauge", "throttle", 420);
        metadata = metadata.WithResource(TimersComponentDefinition.Resources.Clock, "Clock", 0, "Optional keyed clock for deterministic timer scheduling and diagnostics.", designValueType: nameof(TimeProvider), ownership: ResourceDesignMetadataAttributeValues.HostOwned, pickerKind: ResourceDesignMetadataAttributeValues.Clock, keyPattern: "Resources.{name}");
        metadata = metadata.WithOption(TimersComponentDefinition.Options.Interval, OptionValueKind.Duration, "Interval", "Minimum delay between emitted messages.", section: "Timing", importance: OptionDesignMetadataAttributeValues.Primary);
        metadata = metadata.WithOption(TimersComponentDefinition.Options.EmitFirstImmediately, OptionValueKind.Boolean, "Emit First Immediately", "Emit the first input immediately before applying the throttle interval.", defaultValue: true, section: "Timing", importance: OptionDesignMetadataAttributeValues.Advanced);
        metadata = metadata.WithOption(TimersComponentDefinition.Options.BoundedCapacity, OptionValueKind.Number, "Bounded Capacity", "Capacity used for bounded timer work and reliable normal-data output.", defaultValue: 128, min: 1, section: "Runtime", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Number);
        metadata = metadata
            .WithPort(TimersComponentDefinition.Ports.Input, PortDirection.Input, "Input", "Messages", 0, "Schema-less JSON value.", true)
            .WithPort(TimersComponentDefinition.Ports.Output, PortDirection.Output, "Output", "Messages", 1, "Rate-limited JSON value; failures use the message error case.", true)
            .WithPort(TimersComponentDefinition.Ports.Events, PortDirection.Output, "Events", "Diagnostics", 2, "Best-effort throttle diagnostics.");
        return metadata;
    }

    private static ComponentDesignMetadata DescribeDebounce(ComponentDesignMetadata metadata)
    {
        metadata = metadata.WithDisplay("Debounce", "Timers", "Emits a result for the latest workflow value after a quiet period.", "timer-reset", "debounce", 420);
        metadata = metadata.WithResource(TimersComponentDefinition.Resources.Clock, "Clock", 0, "Optional keyed clock for deterministic timer scheduling and diagnostics.", designValueType: nameof(TimeProvider), ownership: ResourceDesignMetadataAttributeValues.HostOwned, pickerKind: ResourceDesignMetadataAttributeValues.Clock, keyPattern: "Resources.{name}");
        metadata = metadata.WithOption(TimersComponentDefinition.Options.QuietPeriod, OptionValueKind.Duration, "Quiet Period", "Required quiet period before the latest input is emitted.", section: "Timing", importance: OptionDesignMetadataAttributeValues.Primary);
        metadata = metadata.WithOption(TimersComponentDefinition.Options.BoundedCapacity, OptionValueKind.Number, "Bounded Capacity", "Capacity used for bounded timer work and reliable normal-data output.", defaultValue: 128, min: 1, section: "Runtime", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Number);
        metadata = metadata
            .WithPort(TimersComponentDefinition.Ports.Input, PortDirection.Input, "Input", "Messages", 0, "Schema-less JSON value.", true)
            .WithPort(TimersComponentDefinition.Ports.Output, PortDirection.Output, "Output", "Messages", 1, "Debounced JSON value; failures use the message error case.", true)
            .WithPort(TimersComponentDefinition.Ports.Events, PortDirection.Output, "Events", "Diagnostics", 2, "Best-effort debounce diagnostics.");
        return metadata;
    }
}
