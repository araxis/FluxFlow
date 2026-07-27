using System.Text.Json;
using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Composition;
using FluxFlow.Components.Timers.Contracts;

namespace FluxFlow.Components.Timers.Composition;

public static partial class TimersComponentDefinition
{
    public static IReadOnlyCollection<ComponentDesignMetadata> CreateMetadata()
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
            TimersComponentDefinition.Types.Interval,
            "Interval Timer",
            "Emits typed timer ticks on a fixed interval.",
            "timer",
            Options.Interval,
            builder =>
            {
                AddNameOption(builder, Options.Interval);
                builder
                    .AddOption(
                        Options.Interval,
                        OptionValueKind.Duration,
                        displayName: "Interval",
                        helperText: "Delay between ticks.",
                        isRequired: true,
                        attributes: OptionAttributes(
                            "Timing",
                            OptionDesignMetadataAttributeValues.Primary))
                    .AddOption(
                        Options.InitialDelay,
                        OptionValueKind.Duration,
                        displayName: "Initial Delay",
                        helperText: "Optional delay before the first scheduled tick.",
                        defaultValue: TimeSpan.Zero,
                        attributes: OptionAttributes(
                            "Timing",
                            OptionDesignMetadataAttributeValues.Advanced))
                    .AddOption(
                        Options.EmitImmediately,
                        OptionValueKind.Boolean,
                        displayName: "Emit Immediately",
                        helperText: "Emit the first tick immediately when the source starts.",
                        defaultValue: false,
                        attributes: OptionAttributes(
                            "Timing",
                            OptionDesignMetadataAttributeValues.Advanced));
                AddMaxTicksOption(builder);
                AddBoundedCapacityOption(builder);
                AddOutputPort(
                    builder,
                    nameof(TimerIntervalTick),
                    "Interval timer tick.",
                    isPrimary: true);
            });

    private static ComponentDesignMetadata CreateScheduleMetadata()
        => CreateTimerMetadata(
            TimersComponentDefinition.Types.Schedule,
            "Schedule Timer",
            "Emits typed timer ticks from a cron schedule.",
            "calendar-clock",
            "schedule",
            builder =>
            {
                AddNameOption(builder, "schedule");
                builder.AddOption(
                    Options.Cron,
                    OptionValueKind.Text,
                    displayName: "Cron",
                    helperText: "Five- or six-field cron expression. Schedule composition uses UTC unless the host provides a typed time-zone setting.",
                    isRequired: true,
                    attributes: OptionAttributes(
                        "Schedule",
                        OptionDesignMetadataAttributeValues.Primary,
                        OptionDesignMetadataAttributeValues.Text));
                AddMaxTicksOption(builder);
                AddBoundedCapacityOption(builder);
                AddOutputPort(
                    builder,
                    nameof(TimerScheduleTick),
                    "Scheduled timer tick.",
                    isPrimary: true);
            },
            attributes: new Dictionary<string, string>
            {
                ["omittedOptions"] = "timeZone",
                ["omittedOptionsReason"] = "TimerScheduleSettings.TimeZone requires typed configuration; this adapter does not add time-zone id conversion."
            });

    private static ComponentDesignMetadata CreateDelayMetadata()
        => CreateTimerMetadata(
            TimersComponentDefinition.Types.Delay,
            "Delay",
            "Emits a result for each workflow value after a configured delay.",
            Resources.Clock,
            Options.Delay,
            builder =>
            {
                AddNameOption(builder, Options.Delay);
                builder.AddOption(
                    Options.Delay,
                    OptionValueKind.Duration,
                    displayName: "Delay",
                    helperText: "Delay applied to each input message.",
                    isRequired: true,
                    attributes: OptionAttributes(
                        "Timing",
                        OptionDesignMetadataAttributeValues.Primary));
                AddBoundedCapacityOption(builder);
                AddTransformPorts(builder);
            });

    private static ComponentDesignMetadata CreateThrottleMetadata()
        => CreateTimerMetadata(
            TimersComponentDefinition.Types.Throttle,
            "Throttle",
            "Rate-limits workflow values and emits ordered results.",
            "gauge",
            "throttle",
            builder =>
            {
                AddNameOption(builder, "throttle");
                builder
                    .AddOption(
                        Options.Interval,
                        OptionValueKind.Duration,
                        displayName: "Interval",
                        helperText: "Minimum delay between emitted messages.",
                        isRequired: true,
                        attributes: OptionAttributes(
                            "Timing",
                            OptionDesignMetadataAttributeValues.Primary))
                    .AddOption(
                        Options.EmitFirstImmediately,
                        OptionValueKind.Boolean,
                        displayName: "Emit First Immediately",
                        helperText: "Emit the first input immediately before applying the throttle interval.",
                        defaultValue: true,
                        attributes: OptionAttributes(
                            "Timing",
                            OptionDesignMetadataAttributeValues.Advanced));
                AddBoundedCapacityOption(builder);
                AddTransformPorts(builder);
            });

    private static ComponentDesignMetadata CreateDebounceMetadata()
        => CreateTimerMetadata(
            TimersComponentDefinition.Types.Debounce,
            "Debounce",
            "Emits a result for the latest workflow value after a quiet period.",
            "timer-reset",
            "debounce",
            builder =>
            {
                AddNameOption(builder, "debounce");
                builder.AddOption(
                    Options.QuietPeriod,
                    OptionValueKind.Duration,
                    displayName: "Quiet Period",
                    helperText: "Required quiet period before the latest input is emitted.",
                    isRequired: true,
                    attributes: OptionAttributes(
                        "Timing",
                        OptionDesignMetadataAttributeValues.Primary));
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
                TimersComponentDefinition.Resources.Clock,
                displayName: "Clock",
                order: 0,
                summary: "Optional keyed clock for deterministic timer scheduling and diagnostics.",
                valueType: nameof(TimeProvider),
                attributes: ResourceDesignMetadataAttributes.CreateHostOwned(
                    ResourceDesignMetadataAttributeValues.Clock,
                    keyPattern: "Resources.{name}"));

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
            Options.Name,
            OptionValueKind.Text,
            displayName: "Name",
            helperText: "Name emitted in timer diagnostics and payloads.",
            defaultValue: defaultValue,
            attributes: OptionAttributes(
                "Diagnostics",
                OptionDesignMetadataAttributeValues.Advanced,
                OptionDesignMetadataAttributeValues.Text));

    private static void AddMaxTicksOption(ComponentDesignMetadataBuilder builder)
        => builder.AddOption(
            Options.MaxTicks,
            OptionValueKind.Number,
            displayName: "Max Ticks",
            helperText: "Optional maximum number of ticks to emit before completing.",
            min: 1,
            attributes: OptionAttributes(
                "Runtime",
                OptionDesignMetadataAttributeValues.Advanced,
                OptionDesignMetadataAttributeValues.Number));

    private static void AddBoundedCapacityOption(ComponentDesignMetadataBuilder builder)
        => builder.AddOption(OptionDesignMetadataFactory.BoundedCapacity(
            128,
            "Maximum queued messages."));

    private static IReadOnlyDictionary<string, string> OptionAttributes(
        string section,
        string importance,
        string? editor = null)
        => OptionDesignMetadataAttributes.Create(
            section: section,
            importance: importance,
            editor: editor);

    private static void AddTransformPorts(ComponentDesignMetadataBuilder builder)
    {
        builder.AddInputPort(
            TimersComponentDefinition.Ports.Input,
            displayName: Ports.Input,
            group: "Messages",
            order: 0,
            summary: "Schema-less JSON value.",
            valueType: nameof(JsonElement),
            isPrimary: true);
        AddOutputPort(
            builder,
            nameof(JsonElement),
            "Delayed or rate-limited JSON value; failures use the message error case.",
            isPrimary: true);
    }

    private static void AddOutputPort(
        ComponentDesignMetadataBuilder builder,
        string valueType,
        string summary,
        bool isPrimary)
        => builder.AddOutputPort(
            TimersComponentDefinition.Ports.Output,
            displayName: Ports.Output,
            group: "Messages",
            order: 1,
            summary: summary,
            valueType: valueType,
            isPrimary: isPrimary);


    public static class Options
    {
        public const string Name = "name";
        public const string Interval = "interval";
        public const string InitialDelay = "initialDelay";
        public const string EmitImmediately = "emitImmediately";
        public const string MaxTicks = "maxTicks";
        public const string BoundedCapacity = "boundedCapacity";
        public const string Cron = "cron";
        public const string Delay = "delay";
        public const string EmitFirstImmediately = "emitFirstImmediately";
        public const string QuietPeriod = "quietPeriod";
    }

    internal static IReadOnlyCollection<ComponentOptionMetadata> CreateOptions(string type)
        => type switch
        {
            Types.Interval =>
            [
                ComponentOptions.Metadata<string>(Options.Name),
                ComponentOptions.Metadata<TimeSpan>(Options.Interval, isRequired: true),
                ComponentOptions.Metadata<TimeSpan>(Options.InitialDelay),
                ComponentOptions.Metadata<bool>(Options.EmitImmediately),
                ComponentOptions.Metadata<long?>(Options.MaxTicks),
                ComponentOptions.Metadata<int>(Options.BoundedCapacity)
            ],
            Types.Schedule =>
            [
                ComponentOptions.Metadata<string>(Options.Name),
                ComponentOptions.Metadata<string>(Options.Cron, isRequired: true),
                ComponentOptions.Metadata<long?>(Options.MaxTicks),
                ComponentOptions.Metadata<int>(Options.BoundedCapacity)
            ],
            Types.Delay =>
            [
                ComponentOptions.Metadata<string>(Options.Name),
                ComponentOptions.Metadata<TimeSpan>(Options.Delay, isRequired: true),
                ComponentOptions.Metadata<int>(Options.BoundedCapacity)
            ],
            Types.Throttle =>
            [
                ComponentOptions.Metadata<string>(Options.Name),
                ComponentOptions.Metadata<TimeSpan>(Options.Interval, isRequired: true),
                ComponentOptions.Metadata<bool>(Options.EmitFirstImmediately),
                ComponentOptions.Metadata<int>(Options.BoundedCapacity)
            ],
            Types.Debounce =>
            [
                ComponentOptions.Metadata<string>(Options.Name),
                ComponentOptions.Metadata<TimeSpan>(Options.QuietPeriod, isRequired: true),
                ComponentOptions.Metadata<int>(Options.BoundedCapacity)
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown component type.")
        };

    internal static IReadOnlyCollection<ComponentResourceMetadata> CreateResources(string type)
        => type switch
        {
            Types.Interval =>
            [
                ComponentResources.Metadata<TimeProvider>(Resources.Clock)
            ],
            Types.Schedule =>
            [
                ComponentResources.Metadata<TimeProvider>(Resources.Clock)
            ],
            Types.Delay =>
            [
                ComponentResources.Metadata<TimeProvider>(Resources.Clock)
            ],
            Types.Throttle =>
            [
                ComponentResources.Metadata<TimeProvider>(Resources.Clock)
            ],
            Types.Debounce =>
            [
                ComponentResources.Metadata<TimeProvider>(Resources.Clock)
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown component type.")
        };

    public static class Types
    {
        public const string Interval = "timer.interval";
    
        public const string Schedule = "timer.schedule";
    
        public const string Delay = "timer.delay";
    
        public const string Throttle = "timer.throttle";
    
        public const string Debounce = "timer.debounce";
    }

    public static class Ports
    {
        public const string Input = "Input";
    
        public const string Output = "Output";
    }

    public static class Resources
    {
        public const string Clock = "clock";
    }
}
