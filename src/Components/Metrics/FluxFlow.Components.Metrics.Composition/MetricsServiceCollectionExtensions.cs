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
        return builder.AddComponent(MetricsComponents.MetricAggregation);
    }

    internal static void ConfigureAggregate(RuntimeComponentRegistrationBuilder component)
    {
        component.AddOption<double>(MetricsComponentDefinition.Options.RateWindowSeconds);
        component.AddOption<int>(MetricsComponentDefinition.Options.BoundedCapacity);
        component.AddOption<int>(MetricsComponentDefinition.Options.MaxGroups);
        component.AddOption<bool>(MetricsComponentDefinition.Options.EmitEverySample);
        component.AddOption<bool>(MetricsComponentDefinition.Options.TrackLatest);
        component.AddOption<bool>(MetricsComponentDefinition.Options.TrackMinMax);
        component.AddOption<bool>(MetricsComponentDefinition.Options.TrackSize);
        component.AddOption<string>(MetricsComponentDefinition.Options.GroupByTag);
        component.AddOption<bool>(MetricsComponentDefinition.Options.TreatMissingValueAsZero);
        component.AddResource<TimeProvider>(MetricsComponentDefinition.Resources.Clock);
        component
            .UseFactory(CreateMetricsAggregateNode)
            .HasStandardPorts(new FluxFlow.Components.Metrics.Materialization.MetricSampleInputMaterializer().InputShape);
    }

    private static MetricsAggregateNode CreateMetricsAggregateNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<MetricsAggregateOptions>();
        var clock = context.GetResource<TimeProvider>(
            MetricsComponentDefinition.Resources.Clock);
        return new MetricsAggregateNode(options, clock);
    }
}
