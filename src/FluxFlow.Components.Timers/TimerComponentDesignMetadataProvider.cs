using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Engine.Definitions;

namespace FluxFlow.Components.Timers;

public sealed class TimerComponentDesignMetadataProvider : IComponentDesignMetadataProvider
{
    public IReadOnlyCollection<ComponentDesignMetadata> GetMetadata() =>
    [
        Source(TimerComponentTypes.Interval, "Timer Interval", "timerInterval",
            "Emits timer ticks at a fixed interval.",
            [
                Number("intervalMilliseconds", "Interval ms", 1000, 1),
                Number("initialDelayMilliseconds", "Initial delay ms", 0, 0),
                Boolean("emitImmediately", "Emit immediately", true),
                Capacity()
            ],
            "TimerTick"),
        Source(TimerComponentTypes.Schedule, "Scheduled Timer", "timerSchedule",
            "Emits schedule ticks from a cron expression.",
            [
                Text("cron", "Cron", "Cron expression.", true, "*/5 * * * *"),
                Text("timeZoneId", "Time zone", defaultValue: "UTC"),
                Capacity()
            ],
            "ScheduleTick"),
        Transform(TimerComponentTypes.Delay, "Delay", "timerDelay",
            "Delays inputs and emits them unchanged.",
            [
                Text("inputType", "Input type", defaultValue: "object"),
                Number("delayMilliseconds", "Delay ms", 1000, 1),
                Capacity()
            ]),
        Transform(TimerComponentTypes.Debounce, "Debounce", "timerDebounce",
            "Emits the latest input after a quiet period.",
            [
                Text("inputType", "Input type", defaultValue: "object"),
                Number("quietPeriodMilliseconds", "Quiet period ms", 500, 1),
                Capacity()
            ]),
        Transform(TimerComponentTypes.Throttle, "Throttle", "timerThrottle",
            "Limits input emissions to a fixed interval.",
            [
                Text("inputType", "Input type", defaultValue: "object"),
                Number("intervalMilliseconds", "Interval ms", 1000, 1),
                Boolean("emitFirstImmediately", "Emit first immediately", true),
                Capacity()
            ])
    ];

    private static ComponentDesignMetadata Source(
        NodeType type,
        string displayName,
        string preferredNodeName,
        string summary,
        IReadOnlyList<OptionDesignMetadata> options,
        string outputType) => new()
        {
            Type = type,
            DisplayName = displayName,
            Category = "Timers",
            Summary = summary,
            IconKey = "timer",
            PreferredNodeName = preferredNodeName,
            SuggestedEditorWidth = 420,
            Options = options,
            Ports =
            [
                Port(TimerComponentPorts.Output, PortDirection.Output, outputType, true),
                Port(TimerComponentPorts.Errors, PortDirection.Output, "FlowError", false, 1)
            ]
        };

    private static ComponentDesignMetadata Transform(
        NodeType type,
        string displayName,
        string preferredNodeName,
        string summary,
        IReadOnlyList<OptionDesignMetadata> options) => new()
        {
            Type = type,
            DisplayName = displayName,
            Category = "Timers",
            Summary = summary,
            IconKey = "timer-control",
            PreferredNodeName = preferredNodeName,
            SuggestedEditorWidth = 420,
            Options = options,
            Ports =
            [
                Port(TimerComponentPorts.Input, PortDirection.Input, "Configured input type", true),
                Port(TimerComponentPorts.Output, PortDirection.Output, "Configured input type", true, 1),
                Port(TimerComponentPorts.Errors, PortDirection.Output, "FlowError", false, 2)
            ]
        };

    private static OptionDesignMetadata Text(
        string name,
        string displayName,
        string? helperText = null,
        bool required = false,
        object? defaultValue = null) => new()
        {
            Name = name,
            Kind = OptionValueKind.Text,
            DisplayName = displayName,
            HelperText = helperText,
            IsRequired = required,
            DefaultValue = defaultValue
        };

    private static OptionDesignMetadata Number(string name, string displayName, object defaultValue, double min) => new()
    {
        Name = name,
        Kind = OptionValueKind.Number,
        DisplayName = displayName,
        DefaultValue = defaultValue,
        Min = min
    };

    private static OptionDesignMetadata Boolean(string name, string displayName, bool defaultValue) => new()
    {
        Name = name,
        Kind = OptionValueKind.Boolean,
        DisplayName = displayName,
        DefaultValue = defaultValue
    };

    private static OptionDesignMetadata Capacity() => Number("boundedCapacity", "Capacity", 128, 1);

    private static PortDesignMetadata Port(
        string name,
        PortDirection direction,
        string valueType,
        bool primary,
        int order = 0) => new()
        {
            Name = new PortName(name),
            Direction = direction,
            ValueType = valueType,
            IsPrimary = primary,
            Order = order
        };
}
