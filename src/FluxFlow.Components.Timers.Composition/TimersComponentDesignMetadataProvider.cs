using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.Timers.Contracts;

namespace FluxFlow.Components.Timers.Composition;

public sealed class TimersComponentDesignMetadataProvider : IComponentDesignMetadataProvider
{
    public IReadOnlyCollection<ComponentDesignMetadata> GetMetadata()
        =>
        [
            CreateIntervalMetadata(),
            CreateScheduleMetadata(),
            CreateDelayMetadata(),
            CreateThrottleMetadata(),
            CreateDebounceMetadata()
        ];

    private static ComponentDesignMetadata CreateIntervalMetadata()
        => CreateTimerMetadata(
            TimersCompositionNodeTypes.Interval,
            "Interval Timer",
            "Emits tick messages on a fixed interval.",
            "timer",
            "interval",
            [
                NameOption("interval"),
                new OptionDesignMetadata
                {
                    Name = "interval",
                    Kind = OptionValueKind.Duration,
                    DisplayName = "Interval",
                    HelperText = "Delay between ticks.",
                    IsRequired = true
                },
                new OptionDesignMetadata
                {
                    Name = "initialDelay",
                    Kind = OptionValueKind.Duration,
                    DisplayName = "Initial Delay",
                    DefaultValue = TimeSpan.Zero,
                    HelperText = "Optional delay before the first scheduled tick."
                },
                new OptionDesignMetadata
                {
                    Name = "emitImmediately",
                    Kind = OptionValueKind.Boolean,
                    DisplayName = "Emit Immediately",
                    DefaultValue = false,
                    HelperText = "Emit the first tick immediately when the source starts."
                },
                MaxTicksOption(),
                BoundedCapacityOption()
            ],
            [
                OutputPort(nameof(TimerTick), "Timer tick message.", isPrimary: true)
            ]);

    private static ComponentDesignMetadata CreateScheduleMetadata()
        => CreateTimerMetadata(
            TimersCompositionNodeTypes.Schedule,
            "Schedule Timer",
            "Emits tick messages from a cron schedule.",
            "calendar-clock",
            "schedule",
            [
                NameOption("schedule"),
                new OptionDesignMetadata
                {
                    Name = "cron",
                    Kind = OptionValueKind.Text,
                    DisplayName = "Cron",
                    HelperText = "Five- or six-field cron expression. Schedule composition uses UTC unless the host provides a typed time-zone setting.",
                    IsRequired = true
                },
                MaxTicksOption(),
                BoundedCapacityOption()
            ],
            [
                OutputPort(nameof(ScheduleTick), "Schedule tick message.", isPrimary: true)
            ],
            new Dictionary<string, string>
            {
                ["omittedOptions"] = "timeZone",
                ["omittedOptionsReason"] = "TimerScheduleSettings.TimeZone requires typed configuration; this adapter does not add time-zone id conversion."
            });

    private static ComponentDesignMetadata CreateDelayMetadata()
        => CreateTimerMetadata(
            TimersCompositionNodeTypes.Delay,
            "Delay",
            "Re-emits each input message after a configured delay.",
            "clock",
            "delay",
            [
                NameOption("delay"),
                new OptionDesignMetadata
                {
                    Name = "delay",
                    Kind = OptionValueKind.Duration,
                    DisplayName = "Delay",
                    HelperText = "Delay applied to each input message.",
                    IsRequired = true
                },
                BoundedCapacityOption()
            ],
            TransformPorts());

    private static ComponentDesignMetadata CreateThrottleMetadata()
        => CreateTimerMetadata(
            TimersCompositionNodeTypes.Throttle,
            "Throttle",
            "Rate-limits input messages to one output per interval.",
            "gauge",
            "throttle",
            [
                NameOption("throttle"),
                new OptionDesignMetadata
                {
                    Name = "interval",
                    Kind = OptionValueKind.Duration,
                    DisplayName = "Interval",
                    HelperText = "Minimum delay between emitted messages.",
                    IsRequired = true
                },
                new OptionDesignMetadata
                {
                    Name = "emitFirstImmediately",
                    Kind = OptionValueKind.Boolean,
                    DisplayName = "Emit First Immediately",
                    DefaultValue = true,
                    HelperText = "Emit the first input immediately before applying the throttle interval."
                },
                BoundedCapacityOption()
            ],
            TransformPorts());

    private static ComponentDesignMetadata CreateDebounceMetadata()
        => CreateTimerMetadata(
            TimersCompositionNodeTypes.Debounce,
            "Debounce",
            "Emits the latest input message after a quiet period.",
            "timer-reset",
            "debounce",
            [
                NameOption("debounce"),
                new OptionDesignMetadata
                {
                    Name = "quietPeriod",
                    Kind = OptionValueKind.Duration,
                    DisplayName = "Quiet Period",
                    HelperText = "Required quiet period before the latest input is emitted.",
                    IsRequired = true
                },
                BoundedCapacityOption()
            ],
            TransformPorts());

    private static ComponentDesignMetadata CreateTimerMetadata(
        string type,
        string displayName,
        string summary,
        string iconKey,
        string preferredNodeName,
        IReadOnlyList<OptionDesignMetadata> options,
        IReadOnlyList<PortDesignMetadata> ports,
        IReadOnlyDictionary<string, string>? attributes = null) => new()
        {
            Type = new ComponentType(type),
            DisplayName = displayName,
            Category = "Timers",
            Summary = summary,
            IconKey = iconKey,
            PreferredNodeName = preferredNodeName,
            SuggestedEditorWidth = 420,
            Options = options,
            Resources = ClockResources(),
            Ports = ports,
            Attributes = attributes ?? new Dictionary<string, string>()
        };

    private static OptionDesignMetadata NameOption(string defaultValue) => new()
    {
        Name = "name",
        Kind = OptionValueKind.Text,
        DisplayName = "Name",
        DefaultValue = defaultValue,
        HelperText = "Name emitted in timer diagnostics and payloads."
    };

    private static OptionDesignMetadata MaxTicksOption() => new()
    {
        Name = "maxTicks",
        Kind = OptionValueKind.Number,
        DisplayName = "Max Ticks",
        Min = 1,
        HelperText = "Optional maximum number of ticks to emit before completing."
    };

    private static OptionDesignMetadata BoundedCapacityOption() => new()
    {
        Name = "boundedCapacity",
        Kind = OptionValueKind.Number,
        DisplayName = "Bounded Capacity",
        DefaultValue = 128,
        Min = 1,
        HelperText = "Maximum queued messages."
    };

    private static IReadOnlyList<ResourceDesignMetadata> ClockResources()
        =>
        [
            new ResourceDesignMetadata
            {
                Name = TimersCompositionResourceNames.Clock,
                DisplayName = "Clock",
                Order = 0,
                Summary = "Optional keyed clock for deterministic timer scheduling and diagnostics.",
                ValueType = nameof(TimeProvider)
            }
        ];

    private static IReadOnlyList<PortDesignMetadata> TransformPorts()
        =>
        [
            new PortDesignMetadata
            {
                Name = new ComponentPortName(TimersCompositionPortNames.Input),
                Direction = PortDirection.Input,
                DisplayName = "Input",
                Group = "Messages",
                Order = 0,
                Summary = "Input message.",
                ValueType = "TInput",
                IsPrimary = true
            },
            OutputPort("TInput", "Original input message after timer processing.", isPrimary: true)
        ];

    private static PortDesignMetadata OutputPort(
        string valueType,
        string summary,
        bool isPrimary) => new()
        {
            Name = new ComponentPortName(TimersCompositionPortNames.Output),
            Direction = PortDirection.Output,
            DisplayName = "Output",
            Group = "Messages",
            Order = 1,
            Summary = summary,
            ValueType = valueType,
            IsPrimary = isPrimary
        };
}
