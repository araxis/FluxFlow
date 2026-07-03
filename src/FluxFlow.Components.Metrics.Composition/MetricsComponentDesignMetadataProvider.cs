using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.Metrics.Contracts;
using FluxFlow.Components.Metrics.Options;

namespace FluxFlow.Components.Metrics.Composition;

public sealed class MetricsComponentDesignMetadataProvider : IComponentDesignMetadataProvider
{
    private static readonly MetricsAggregateOptions Defaults = new();

    public IReadOnlyCollection<ComponentDesignMetadata> GetMetadata()
        => [CreateAggregateMetadata()];

    private static ComponentDesignMetadata CreateAggregateMetadata()
    {
        var builder = new ComponentDesignMetadataBuilder(MetricsCompositionNodeTypes.Aggregate)
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
                "rateWindowSeconds",
                OptionValueKind.Number,
                displayName: "Rate Window Seconds",
                helperText: "Rolling window in seconds for current-rate calculations.",
                defaultValue: Defaults.RateWindowSeconds,
                min: 0.000001,
                attributes: OptionAttributes(
                    "Rate",
                    OptionDesignMetadataAttributeValues.Primary,
                    OptionDesignMetadataAttributeValues.Number))
            .AddOption(
                "boundedCapacity",
                OptionValueKind.Number,
                displayName: "Bounded Capacity",
                helperText: "Maximum queued input messages.",
                defaultValue: Defaults.BoundedCapacity,
                min: 1,
                attributes: OptionAttributes(
                    "Runtime",
                    OptionDesignMetadataAttributeValues.Advanced,
                    OptionDesignMetadataAttributeValues.Number))
            .AddOption(
                "maxGroups",
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
                "emitEverySample",
                OptionValueKind.Boolean,
                displayName: "Emit Every Sample",
                helperText: "Emit a snapshot after every accepted sample instead of only at completion.",
                defaultValue: Defaults.EmitEverySample,
                attributes: OptionAttributes(
                    "Emission",
                    OptionDesignMetadataAttributeValues.Advanced))
            .AddOption(
                "trackLatest",
                OptionValueKind.Boolean,
                displayName: "Track Latest",
                helperText: "Include the latest metric sample in snapshots.",
                defaultValue: Defaults.TrackLatest,
                attributes: OptionAttributes(
                    "Snapshot",
                    OptionDesignMetadataAttributeValues.Advanced))
            .AddOption(
                "trackMinMax",
                OptionValueKind.Boolean,
                displayName: "Track Min/Max",
                helperText: "Track minimum and maximum numeric values.",
                defaultValue: Defaults.TrackMinMax,
                attributes: OptionAttributes(
                    "Snapshot",
                    OptionDesignMetadataAttributeValues.Advanced))
            .AddOption(
                "trackSize",
                OptionValueKind.Boolean,
                displayName: "Track Size",
                helperText: "Track total size when samples include size values.",
                defaultValue: Defaults.TrackSize,
                attributes: OptionAttributes(
                    "Snapshot",
                    OptionDesignMetadataAttributeValues.Advanced))
            .AddOption(
                "groupByTag",
                OptionValueKind.Text,
                displayName: "Group By Tag",
                helperText: "Optional tag key used for grouping instead of the sample group.",
                attributes: OptionAttributes(
                    "Grouping",
                    OptionDesignMetadataAttributeValues.Advanced,
                    OptionDesignMetadataAttributeValues.Text))
            .AddOption(
                "treatMissingValueAsZero",
                OptionValueKind.Boolean,
                displayName: "Treat Missing Value As Zero",
                helperText: "Count missing numeric values as zero-valued observations.",
                defaultValue: Defaults.TreatMissingValueAsZero,
                attributes: OptionAttributes(
                    "Aggregation",
                    OptionDesignMetadataAttributeValues.Advanced));

    private static void AddAggregateResources(ComponentDesignMetadataBuilder builder)
        => builder.AddResource(
            MetricsCompositionResourceNames.Clock,
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
                MetricsCompositionPortNames.Input,
                displayName: "Input",
                group: "Messages",
                order: 0,
                summary: "Metric sample to aggregate.",
                valueType: nameof(MetricSampleInput),
                isPrimary: true)
            .AddOutputPort(
                MetricsCompositionPortNames.Output,
                displayName: "Output",
                group: "Results",
                order: 1,
                summary: "Metric aggregate snapshot.",
                valueType: nameof(MetricSnapshotOutput),
                isPrimary: true);
}
