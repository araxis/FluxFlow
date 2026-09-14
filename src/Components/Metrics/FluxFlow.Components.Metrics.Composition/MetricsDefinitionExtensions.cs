using FluxFlow.Components.Metrics.Options;
using FluxFlow.Components.Metrics.Nodes;
using Microsoft.Extensions.DependencyInjection;
using FluxFlow.Components.Metrics.Contracts;
using FluxFlow.Composition;
using FluxFlow.Composition.Definition;

using FluxFlow.Data;

namespace FluxFlow.Components.Metrics.Composition;

public static class MetricsDefinitionExtensions
{
    public static InputOutputComponentHandle<FlowValue, FlowValue> AddMetricAggregation(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<MetricAggregationComponentBuilder>? configure = null)
        => workflow.AddComponent(name, MetricsComponentDefinition.Types.Aggregate,
            MetricsServiceCollectionExtensions.CreateMetricsAggregateNode,
            definition =>
            {
                var options = new MetricAggregationComponentBuilder();
                configure?.Invoke(options);
                options.Apply(definition);
            },
            static component => new InputOutputComponentHandle<FlowValue, FlowValue>(component, MetricsComponentDefinition.Ports.Input, MetricsComponentDefinition.Ports.Output, MetricsComponentDefinition.Ports.Events));

    public static WorkflowDefinitionBuilder AddMetricAggregation(
        this WorkflowDefinitionBuilder workflow,
        string name,
        out InputOutputComponentHandle<FlowValue, FlowValue> aggregation)
    {
        aggregation = workflow.AddMetricAggregation(name);
        return workflow;
    }

    public static WorkflowDefinitionBuilder AddMetricAggregation(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<MetricAggregationComponentBuilder> configure,
        out InputOutputComponentHandle<FlowValue, FlowValue> aggregation)
    {
        ArgumentNullException.ThrowIfNull(configure);
        aggregation = workflow.AddMetricAggregation(name, configure);
        return workflow;
    }
}

public sealed class MetricAggregationComponentBuilder
{
    public double? RateWindowSeconds { get; set; }
    public int? BoundedCapacity { get; set; }
    public int? MaxGroups { get; set; }
    public bool? EmitEverySample { get; set; }
    public bool? TrackLatest { get; set; }
    public bool? TrackMinMax { get; set; }
    public bool? TrackSize { get; set; }
    public string? GroupByTag { get; set; }
    public bool? TreatMissingValueAsZero { get; set; }
    public ResourceHandle<TimeProvider>? Clock { get; set; }

    internal void Apply(ComponentDefinitionBuilder definition)
    {
        Set(definition, MetricsComponentDefinition.Options.RateWindowSeconds, RateWindowSeconds);
        Set(definition, MetricsComponentDefinition.Options.BoundedCapacity, BoundedCapacity);
        Set(definition, MetricsComponentDefinition.Options.MaxGroups, MaxGroups);
        Set(definition, MetricsComponentDefinition.Options.EmitEverySample, EmitEverySample);
        Set(definition, MetricsComponentDefinition.Options.TrackLatest, TrackLatest);
        Set(definition, MetricsComponentDefinition.Options.TrackMinMax, TrackMinMax);
        Set(definition, MetricsComponentDefinition.Options.TrackSize, TrackSize);
        Set(definition, MetricsComponentDefinition.Options.GroupByTag, GroupByTag);
        Set(definition, MetricsComponentDefinition.Options.TreatMissingValueAsZero, TreatMissingValueAsZero);
        if (Clock is not null)
            definition.UseResource(MetricsComponentDefinition.Resources.Clock, Clock);
    }

    private static void Set<T>(ComponentDefinitionBuilder definition, string name, T? value)
    {
        if (value is not null)
            definition.Set(name, value);
    }
}
