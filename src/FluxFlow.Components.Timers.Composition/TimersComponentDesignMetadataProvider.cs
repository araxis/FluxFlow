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
            builder =>
            {
                AddNameOption(builder, "interval");
                builder
                    .AddOption(
                        "interval",
                        OptionValueKind.Duration,
                        displayName: "Interval",
                        helperText: "Delay between ticks.",
                        isRequired: true)
                    .AddOption(
                        "initialDelay",
                        OptionValueKind.Duration,
                        displayName: "Initial Delay",
                        helperText: "Optional delay before the first scheduled tick.",
                        defaultValue: TimeSpan.Zero)
                    .AddOption(
                        "emitImmediately",
                        OptionValueKind.Boolean,
                        displayName: "Emit Immediately",
                        helperText: "Emit the first tick immediately when the source starts.",
                        defaultValue: false);
                AddMaxTicksOption(builder);
                AddBoundedCapacityOption(builder);
                AddOutputPort(
                    builder,
                    nameof(TimerTick),
                    "Timer tick message.",
                    isPrimary: true);
            });

    private static ComponentDesignMetadata CreateScheduleMetadata()
        => CreateTimerMetadata(
            TimersCompositionNodeTypes.Schedule,
            "Schedule Timer",
            "Emits tick messages from a cron schedule.",
            "calendar-clock",
            "schedule",
            builder =>
            {
                AddNameOption(builder, "schedule");
                builder.AddOption(
                    "cron",
                    OptionValueKind.Text,
                    displayName: "Cron",
                    helperText: "Five- or six-field cron expression. Schedule composition uses UTC unless the host provides a typed time-zone setting.",
                    isRequired: true);
                AddMaxTicksOption(builder);
                AddBoundedCapacityOption(builder);
                AddOutputPort(
                    builder,
                    nameof(ScheduleTick),
                    "Schedule tick message.",
                    isPrimary: true);
            },
            attributes: new Dictionary<string, string>
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
            builder =>
            {
                AddNameOption(builder, "delay");
                builder.AddOption(
                    "delay",
                    OptionValueKind.Duration,
                    displayName: "Delay",
                    helperText: "Delay applied to each input message.",
                    isRequired: true);
                AddBoundedCapacityOption(builder);
                AddTransformPorts(builder);
            });

    private static ComponentDesignMetadata CreateThrottleMetadata()
        => CreateTimerMetadata(
            TimersCompositionNodeTypes.Throttle,
            "Throttle",
            "Rate-limits input messages to one output per interval.",
            "gauge",
            "throttle",
            builder =>
            {
                AddNameOption(builder, "throttle");
                builder
                    .AddOption(
                        "interval",
                        OptionValueKind.Duration,
                        displayName: "Interval",
                        helperText: "Minimum delay between emitted messages.",
                        isRequired: true)
                    .AddOption(
                        "emitFirstImmediately",
                        OptionValueKind.Boolean,
                        displayName: "Emit First Immediately",
                        helperText: "Emit the first input immediately before applying the throttle interval.",
                        defaultValue: true);
                AddBoundedCapacityOption(builder);
                AddTransformPorts(builder);
            });

    private static ComponentDesignMetadata CreateDebounceMetadata()
        => CreateTimerMetadata(
            TimersCompositionNodeTypes.Debounce,
            "Debounce",
            "Emits the latest input message after a quiet period.",
            "timer-reset",
            "debounce",
            builder =>
            {
                AddNameOption(builder, "debounce");
                builder.AddOption(
                    "quietPeriod",
                    OptionValueKind.Duration,
                    displayName: "Quiet Period",
                    helperText: "Required quiet period before the latest input is emitted.",
                    isRequired: true);
                AddBoundedCapacityOption(builder);
                AddTransformPorts(builder);
            });

    private static ComponentDesignMetadata CreateTimerMetadata(
        string type,
        string displayName,
        string summary,
        string iconKey,
        string preferredNodeName,
        Action<ComponentDesignMetadataBuilder> configure,
        IReadOnlyDictionary<string, string>? attributes = null)
    {
        var builder = new ComponentDesignMetadataBuilder(type)
            .WithDisplay(
                displayName: displayName,
                category: "Timers",
                summary: summary,
                iconKey: iconKey,
                preferredNodeName: preferredNodeName,
                suggestedEditorWidth: 420)
            .AddResource(
                TimersCompositionResourceNames.Clock,
                displayName: "Clock",
                order: 0,
                summary: "Optional keyed clock for deterministic timer scheduling and diagnostics.",
                valueType: nameof(TimeProvider));

        if (attributes is not null)
        {
            foreach (var attribute in attributes)
            {
                builder.AddAttribute(attribute.Key, attribute.Value);
            }
        }

        configure(builder);

        return builder.Build();
    }

    private static void AddNameOption(
        ComponentDesignMetadataBuilder builder,
        string defaultValue)
        => builder.AddOption(
            "name",
            OptionValueKind.Text,
            displayName: "Name",
            helperText: "Name emitted in timer diagnostics and payloads.",
            defaultValue: defaultValue);

    private static void AddMaxTicksOption(ComponentDesignMetadataBuilder builder)
        => builder.AddOption(
            "maxTicks",
            OptionValueKind.Number,
            displayName: "Max Ticks",
            helperText: "Optional maximum number of ticks to emit before completing.",
            min: 1);

    private static void AddBoundedCapacityOption(ComponentDesignMetadataBuilder builder)
        => builder.AddOption(
            "boundedCapacity",
            OptionValueKind.Number,
            displayName: "Bounded Capacity",
            helperText: "Maximum queued messages.",
            defaultValue: 128,
            min: 1);

    private static void AddTransformPorts(ComponentDesignMetadataBuilder builder)
    {
        builder.AddInputPort(
            TimersCompositionPortNames.Input,
            displayName: "Input",
            group: "Messages",
            order: 0,
            summary: "Input message.",
            valueType: "TInput",
            isPrimary: true);
        AddOutputPort(
            builder,
            "TInput",
            "Original input message after timer processing.",
            isPrimary: true);
    }

    private static void AddOutputPort(
        ComponentDesignMetadataBuilder builder,
        string valueType,
        string summary,
        bool isPrimary)
        => builder.AddOutputPort(
            TimersCompositionPortNames.Output,
            displayName: "Output",
            group: "Messages",
            order: 1,
            summary: summary,
            valueType: valueType,
            isPrimary: isPrimary);
}
