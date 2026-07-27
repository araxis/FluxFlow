using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Composition;
using FluxFlow.Components.Metrics.Contracts;
using FluxFlow.Components.Metrics.Options;

namespace FluxFlow.Components.Metrics.Composition;

public static partial class MetricsComponentDefinition
{
    private static readonly MetricsAggregateOptions Defaults = new();

    public static IReadOnlyCollection<ComponentDesignMetadata> CreateMetadata()
        => [CreateAggregateMetadata()];

    private static ComponentDesignMetadata CreateAggregateMetadata()
    {
        var builder = new ComponentDesignMetadataBuilder(MetricsComponentDefinition.Types.Aggregate)
            .WithDisplay(
                displayName: "Metrics Aggregate",
                category: "Metrics",
                summary: "Folds metric samples into rolling count, value, rate, size, and group snapshots.",
                iconKey: "chart-no-axes-combined",
                preferredNodeName: "aggregateMetrics",
                suggestedEditorWidth: 460);

        AddAggregateOptions(builder);
        AddAggregateResources(builder);
        AddAggregatePorts(builder);

        return builder.Build();
    }

    private static void AddAggregateOptions(ComponentDesignMetadataBuilder builder)
        => builder
            .AddOption(
                Options.RateWindowSeconds,
                OptionValueKind.Number,
                displayName: "Rate Window Seconds",
                helperText: "Rolling window in seconds for current-rate calculations.",
                defaultValue: Defaults.RateWindowSeconds,
                min: 0.000001,
                attributes: OptionAttributes(
                    "Rate",
                    OptionDesignMetadataAttributeValues.Primary,
                    OptionDesignMetadataAttributeValues.Number))
            .AddOption(OptionDesignMetadataFactory.BoundedCapacity(Defaults.BoundedCapacity))
            .AddOption(
                Options.MaxGroups,
                OptionValueKind.Number,
                displayName: "Max Groups",
                helperText: "Maximum number of per-group snapshots to track.",
                defaultValue: Defaults.MaxGroups,
                min: 0,
                attributes: OptionAttributes(
                    "Grouping",
                    OptionDesignMetadataAttributeValues.Advanced,
                    OptionDesignMetadataAttributeValues.Number))
            .AddOption(
                Options.EmitEverySample,
                OptionValueKind.Boolean,
                displayName: "Emit Every Sample",
                helperText: "Emit a snapshot after every accepted sample instead of only at completion.",
                defaultValue: Defaults.EmitEverySample,
                attributes: OptionAttributes(
                    "Emission",
                    OptionDesignMetadataAttributeValues.Advanced))
            .AddOption(
                Options.TrackLatest,
                OptionValueKind.Boolean,
                displayName: "Track Latest",
                helperText: "Include the latest metric sample in snapshots.",
                defaultValue: Defaults.TrackLatest,
                attributes: OptionAttributes(
                    "Snapshot",
                    OptionDesignMetadataAttributeValues.Advanced))
            .AddOption(
                Options.TrackMinMax,
                OptionValueKind.Boolean,
                displayName: "Track Min/Max",
                helperText: "Track minimum and maximum numeric values.",
                defaultValue: Defaults.TrackMinMax,
                attributes: OptionAttributes(
                    "Snapshot",
                    OptionDesignMetadataAttributeValues.Advanced))
            .AddOption(
                Options.TrackSize,
                OptionValueKind.Boolean,
                displayName: "Track Size",
                helperText: "Track total size when samples include size values.",
                defaultValue: Defaults.TrackSize,
                attributes: OptionAttributes(
                    "Snapshot",
                    OptionDesignMetadataAttributeValues.Advanced))
            .AddOption(
                Options.GroupByTag,
                OptionValueKind.Text,
                displayName: "Group By Tag",
                helperText: "Optional tag key used for grouping instead of the sample group.",
                attributes: OptionAttributes(
                    "Grouping",
                    OptionDesignMetadataAttributeValues.Advanced,
                    OptionDesignMetadataAttributeValues.Text))
            .AddOption(
                Options.TreatMissingValueAsZero,
                OptionValueKind.Boolean,
                displayName: "Treat Missing Value As Zero",
                helperText: "Count missing numeric values as zero-valued observations.",
                defaultValue: Defaults.TreatMissingValueAsZero,
                attributes: OptionAttributes(
                    "Aggregation",
                    OptionDesignMetadataAttributeValues.Advanced));

    private static void AddAggregateResources(ComponentDesignMetadataBuilder builder)
        => builder.AddResource(
            MetricsComponentDefinition.Resources.Clock,
            displayName: "Clock",
            order: 0,
            summary: "Optional keyed clock for deterministic metric timestamps and diagnostics.",
            valueType: nameof(TimeProvider),
            attributes: ResourceDesignMetadataAttributes.CreateHostOwned(
                ResourceDesignMetadataAttributeValues.Clock,
                keyPattern: "clock:{name}"));

    private static IReadOnlyDictionary<string, string> OptionAttributes(
        string section,
        string importance,
        string? editor = null)
        => OptionDesignMetadataAttributes.Create(
            section: section,
            importance: importance,
            editor: editor);

    private static void AddAggregatePorts(ComponentDesignMetadataBuilder builder)
        => builder
            .AddInputPort(
                MetricsComponentDefinition.Ports.Input,
                displayName: Ports.Input,
                group: "Messages",
                order: 0,
                summary: "Metric sample to aggregate.",
                valueType: nameof(MetricSampleInput),
                isPrimary: true)
            .AddOutputPort(
                MetricsComponentDefinition.Ports.Output,
                displayName: Ports.Output,
                group: "Results",
                order: 1,
                summary: "Metric aggregate snapshot or expected aggregation failure.",
                valueType: nameof(MetricSnapshotOutput),
                isPrimary: true);


    public static class Options
    {
        public const string RateWindowSeconds = "rateWindowSeconds";
        public const string BoundedCapacity = "boundedCapacity";
        public const string MaxGroups = "maxGroups";
        public const string EmitEverySample = "emitEverySample";
        public const string TrackLatest = "trackLatest";
        public const string TrackMinMax = "trackMinMax";
        public const string TrackSize = "trackSize";
        public const string GroupByTag = "groupByTag";
        public const string TreatMissingValueAsZero = "treatMissingValueAsZero";
    }

    internal static IReadOnlyCollection<ComponentOptionMetadata> CreateOptions(string type)
        => type switch
        {
            Types.Aggregate =>
            [
                ComponentOptions.Metadata<double>(Options.RateWindowSeconds),
                ComponentOptions.Metadata<int>(Options.BoundedCapacity),
                ComponentOptions.Metadata<int>(Options.MaxGroups),
                ComponentOptions.Metadata<bool>(Options.EmitEverySample),
                ComponentOptions.Metadata<bool>(Options.TrackLatest),
                ComponentOptions.Metadata<bool>(Options.TrackMinMax),
                ComponentOptions.Metadata<bool>(Options.TrackSize),
                ComponentOptions.Metadata<string>(Options.GroupByTag),
                ComponentOptions.Metadata<bool>(Options.TreatMissingValueAsZero)
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown component type.")
        };

    internal static IReadOnlyCollection<ComponentResourceMetadata> CreateResources(string type)
        => type switch
        {
            Types.Aggregate =>
            [
                ComponentResources.Metadata<TimeProvider>(Resources.Clock)
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown component type.")
        };

    public static class Types
    {
        public const string Aggregate = "metric.aggregate";
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
