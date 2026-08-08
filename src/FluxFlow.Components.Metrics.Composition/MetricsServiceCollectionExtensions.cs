using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.Metrics.Contracts;
using FluxFlow.Components.Metrics.Nodes;
using FluxFlow.Components.Metrics.Options;
using FluxFlow.Composition;
using FluxFlow.Data;

namespace FluxFlow.Components.Metrics.Composition;

public static class MetricsServiceCollectionExtensions
{
    public static FluxFlowRegistrationBuilder AddMetrics(this FluxFlowRegistrationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddDesignedComponent(MetricsComponents.MetricAggregation);
    }

    internal static void ConfigureAggregate(ComponentRegistrationBuilder component)
    {
        var defaults = new MetricsAggregateOptions();
        component.WithDisplay("Metrics Aggregate", "Metrics", "Folds metric samples into rolling count, value, rate, size, and group snapshots.", "chart-no-axes-combined", "aggregateMetrics", 460);
        component
            .UseFactory(CreateMetricsAggregateNode)
            .HasInput(MetricsComponentDefinition.Ports.Input, static node => node.Input, "Input", "Messages", 0, "Metric sample to aggregate.", true)
            .HasOutput(MetricsComponentDefinition.Ports.Output, static node => node.Output, "Output", "Results", 1, "Metric aggregate snapshot or expected aggregation failure.", true)
            .HasEvents(MetricsComponentDefinition.Ports.Events, static node => node.Events, "Events", "Diagnostics", 2, "Best-effort metrics diagnostics.");
        component.AddOption<double>(MetricsComponentDefinition.Options.RateWindowSeconds, OptionValueKind.Number, "Rate Window Seconds", "Rolling window in seconds for current-rate calculations.", defaultValue: defaults.RateWindowSeconds, min: 0.000001, section: "Rate", importance: OptionDesignMetadataAttributeValues.Primary, editor: OptionDesignMetadataAttributeValues.Number);
        AddNumber(component, MetricsComponentDefinition.Options.BoundedCapacity, "Bounded Capacity", "Capacity used for bounded processing and reliable normal-data output.", defaults.BoundedCapacity, 1, "Runtime");
        AddNumber(component, MetricsComponentDefinition.Options.MaxGroups, "Max Groups", "Maximum number of per-group snapshots to track.", defaults.MaxGroups, 0, "Grouping");
        AddBoolean(component, MetricsComponentDefinition.Options.EmitEverySample, "Emit Every Sample", "Emit a snapshot after every accepted sample instead of only at completion.", defaults.EmitEverySample, "Emission");
        AddBoolean(component, MetricsComponentDefinition.Options.TrackLatest, "Track Latest", "Include the latest metric sample in snapshots.", defaults.TrackLatest, "Snapshot");
        AddBoolean(component, MetricsComponentDefinition.Options.TrackMinMax, "Track Min/Max", "Track minimum and maximum numeric values.", defaults.TrackMinMax, "Snapshot");
        AddBoolean(component, MetricsComponentDefinition.Options.TrackSize, "Track Size", "Track total size when samples include size values.", defaults.TrackSize, "Snapshot");
        component.AddOption<string>(MetricsComponentDefinition.Options.GroupByTag, OptionValueKind.Text, "Group By Tag", "Optional tag key used for grouping instead of the sample group.", section: "Grouping", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Text);
        AddBoolean(component, MetricsComponentDefinition.Options.TreatMissingValueAsZero, "Treat Missing Value As Zero", "Count missing numeric values as zero-valued observations.", defaults.TreatMissingValueAsZero, "Aggregation");
        component.AddResource<TimeProvider>(MetricsComponentDefinition.Resources.Clock, "Clock", 0, "Optional keyed clock for deterministic metric timestamps and diagnostics.", designValueType: nameof(TimeProvider), ownership: ResourceDesignMetadataAttributeValues.HostOwned, pickerKind: ResourceDesignMetadataAttributeValues.Clock, keyPattern: "clock:{name}");
    }

    private static void AddNumber(ComponentRegistrationBuilder component, string name, string displayName, string helperText, int defaultValue, double min, string section)
        => component.AddOption<int>(name, OptionValueKind.Number, displayName, helperText, defaultValue: defaultValue, min: min, section: section, importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Number);

    private static void AddBoolean(ComponentRegistrationBuilder component, string name, string displayName, string helperText, bool defaultValue, string section)
        => component.AddOption<bool>(name, OptionValueKind.Boolean, displayName, helperText, defaultValue: defaultValue, section: section, importance: OptionDesignMetadataAttributeValues.Advanced);

    private static MetricsAggregateNode CreateMetricsAggregateNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<MetricsAggregateOptions>();
        var clock = context.GetResource<TimeProvider>(
            MetricsComponentDefinition.Resources.Clock);
        return new MetricsAggregateNode(options, clock);
    }
}
