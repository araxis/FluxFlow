using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.Metrics.Composition;
using FluxFlow.Components.Metrics.Options;
using FluxFlow.Composition;

namespace FluxFlow.Components.Metrics.Designer;

public static class MetricsDesignExtensions
{
    public static FluxFlowRegistrationBuilder AddMetricsDesign(this FluxFlowRegistrationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder
            .AddDesignMetadata(MetricsComponentDefinition.Types.Aggregate, DescribeAggregate);
    }

    private static ComponentDesignMetadata DescribeAggregate(ComponentDesignMetadata metadata)
    {
        var defaults = new MetricsAggregateOptions();
        metadata = metadata.WithDisplay("Metrics Aggregate", "Metrics", "Folds metric samples into rolling count, value, rate, size, and group snapshots.", "chart-no-axes-combined", "aggregateMetrics", 460);
        metadata = metadata
            .WithPort(MetricsComponentDefinition.Ports.Input, PortDirection.Input, "Input", "Messages", 0, "Metric sample to aggregate.", true)
            .WithPort(MetricsComponentDefinition.Ports.Output, PortDirection.Output, "Output", "Results", 1, "Metric aggregate snapshot or expected aggregation failure.", true)
            .WithPort(MetricsComponentDefinition.Ports.Events, PortDirection.Output, "Events", "Diagnostics", 2, "Best-effort metrics diagnostics.");
        metadata = metadata.WithOption(MetricsComponentDefinition.Options.RateWindowSeconds, OptionValueKind.Number, "Rate Window Seconds", "Rolling window in seconds for current-rate calculations.", defaultValue: defaults.RateWindowSeconds, min: 0.000001, section: "Rate", importance: OptionDesignMetadataAttributeValues.Primary, editor: OptionDesignMetadataAttributeValues.Number);
        metadata = metadata.WithOption(MetricsComponentDefinition.Options.BoundedCapacity, OptionValueKind.Number, "Bounded Capacity", "Capacity used for bounded processing and reliable normal-data output.", defaultValue: defaults.BoundedCapacity, min: 1, section: "Runtime", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Number);
        metadata = metadata.WithOption(MetricsComponentDefinition.Options.MaxGroups, OptionValueKind.Number, "Max Groups", "Maximum number of per-group snapshots to track.", defaultValue: defaults.MaxGroups, min: 0, section: "Grouping", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Number);
        metadata = metadata.WithOption(MetricsComponentDefinition.Options.EmitEverySample, OptionValueKind.Boolean, "Emit Every Sample", "Emit a snapshot after every accepted sample instead of only at completion.", defaultValue: defaults.EmitEverySample, section: "Emission", importance: OptionDesignMetadataAttributeValues.Advanced);
        metadata = metadata.WithOption(MetricsComponentDefinition.Options.TrackLatest, OptionValueKind.Boolean, "Track Latest", "Include the latest metric sample in snapshots.", defaultValue: defaults.TrackLatest, section: "Snapshot", importance: OptionDesignMetadataAttributeValues.Advanced);
        metadata = metadata.WithOption(MetricsComponentDefinition.Options.TrackMinMax, OptionValueKind.Boolean, "Track Min/Max", "Track minimum and maximum numeric values.", defaultValue: defaults.TrackMinMax, section: "Snapshot", importance: OptionDesignMetadataAttributeValues.Advanced);
        metadata = metadata.WithOption(MetricsComponentDefinition.Options.TrackSize, OptionValueKind.Boolean, "Track Size", "Track total size when samples include size values.", defaultValue: defaults.TrackSize, section: "Snapshot", importance: OptionDesignMetadataAttributeValues.Advanced);
        metadata = metadata.WithOption(MetricsComponentDefinition.Options.GroupByTag, OptionValueKind.Text, "Group By Tag", "Optional tag key used for grouping instead of the sample group.", section: "Grouping", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Text);
        metadata = metadata.WithOption(MetricsComponentDefinition.Options.TreatMissingValueAsZero, OptionValueKind.Boolean, "Treat Missing Value As Zero", "Count missing numeric values as zero-valued observations.", defaultValue: defaults.TreatMissingValueAsZero, section: "Aggregation", importance: OptionDesignMetadataAttributeValues.Advanced);
        metadata = metadata.WithResource(MetricsComponentDefinition.Resources.Clock, "Clock", 0, "Optional keyed clock for deterministic metric timestamps and diagnostics.", designValueType: nameof(TimeProvider), ownership: ResourceDesignMetadataAttributeValues.HostOwned, pickerKind: ResourceDesignMetadataAttributeValues.Clock, keyPattern: "clock:{name}");
        return metadata;
    }
}
