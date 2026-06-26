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

    private static ComponentDesignMetadata CreateAggregateMetadata() => new()
    {
        Type = new ComponentType(MetricsCompositionNodeTypes.Aggregate),
        DisplayName = "Metrics Aggregate",
        Category = "Metrics",
        Summary = "Folds metric samples into rolling count, value, rate, size, and group snapshots.",
        IconKey = "chart-no-axes-combined",
        PreferredNodeName = "aggregateMetrics",
        SuggestedEditorWidth = 460,
        Options = AggregateOptionsMetadata(),
        Resources = AggregateResources(),
        Ports = AggregatePorts()
    };

    private static IReadOnlyList<OptionDesignMetadata> AggregateOptionsMetadata()
        =>
        [
            new OptionDesignMetadata
            {
                Name = "rateWindowSeconds",
                Kind = OptionValueKind.Number,
                DisplayName = "Rate Window Seconds",
                DefaultValue = Defaults.RateWindowSeconds,
                Min = 0.000001,
                HelperText = "Rolling window in seconds for current-rate calculations."
            },
            new OptionDesignMetadata
            {
                Name = "boundedCapacity",
                Kind = OptionValueKind.Number,
                DisplayName = "Bounded Capacity",
                DefaultValue = Defaults.BoundedCapacity,
                Min = 1,
                HelperText = "Maximum queued input messages."
            },
            new OptionDesignMetadata
            {
                Name = "maxGroups",
                Kind = OptionValueKind.Number,
                DisplayName = "Max Groups",
                DefaultValue = Defaults.MaxGroups,
                Min = 0,
                HelperText = "Maximum number of per-group snapshots to track."
            },
            new OptionDesignMetadata
            {
                Name = "emitEverySample",
                Kind = OptionValueKind.Boolean,
                DisplayName = "Emit Every Sample",
                DefaultValue = Defaults.EmitEverySample,
                HelperText = "Emit a snapshot after every accepted sample instead of only at completion."
            },
            new OptionDesignMetadata
            {
                Name = "trackLatest",
                Kind = OptionValueKind.Boolean,
                DisplayName = "Track Latest",
                DefaultValue = Defaults.TrackLatest,
                HelperText = "Include the latest metric sample in snapshots."
            },
            new OptionDesignMetadata
            {
                Name = "trackMinMax",
                Kind = OptionValueKind.Boolean,
                DisplayName = "Track Min/Max",
                DefaultValue = Defaults.TrackMinMax,
                HelperText = "Track minimum and maximum numeric values."
            },
            new OptionDesignMetadata
            {
                Name = "trackSize",
                Kind = OptionValueKind.Boolean,
                DisplayName = "Track Size",
                DefaultValue = Defaults.TrackSize,
                HelperText = "Track total size when samples include size values."
            },
            new OptionDesignMetadata
            {
                Name = "groupByTag",
                Kind = OptionValueKind.Text,
                DisplayName = "Group By Tag",
                HelperText = "Optional tag key used for grouping instead of the sample group."
            },
            new OptionDesignMetadata
            {
                Name = "treatMissingValueAsZero",
                Kind = OptionValueKind.Boolean,
                DisplayName = "Treat Missing Value As Zero",
                DefaultValue = Defaults.TreatMissingValueAsZero,
                HelperText = "Count missing numeric values as zero-valued observations."
            }
        ];

    private static IReadOnlyList<ResourceDesignMetadata> AggregateResources()
        =>
        [
            new ResourceDesignMetadata
            {
                Name = MetricsCompositionResourceNames.Clock,
                DisplayName = "Clock",
                Order = 0,
                Summary = "Optional keyed clock for deterministic metric timestamps and diagnostics.",
                ValueType = nameof(TimeProvider)
            }
        ];

    private static IReadOnlyList<PortDesignMetadata> AggregatePorts()
        =>
        [
            new PortDesignMetadata
            {
                Name = new ComponentPortName(MetricsCompositionPortNames.Input),
                Direction = PortDirection.Input,
                DisplayName = "Input",
                Group = "Messages",
                Order = 0,
                Summary = "Metric sample to aggregate.",
                ValueType = nameof(MetricSampleInput),
                IsPrimary = true
            },
            new PortDesignMetadata
            {
                Name = new ComponentPortName(MetricsCompositionPortNames.Output),
                Direction = PortDirection.Output,
                DisplayName = "Output",
                Group = "Results",
                Order = 1,
                Summary = "Metric aggregate snapshot.",
                ValueType = nameof(MetricSnapshotOutput),
                IsPrimary = true
            }
        ];
}
