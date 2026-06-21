using FluxFlow.Components.Metrics.Contracts;
using FluxFlow.Components.Metrics.Nodes;
using FluxFlow.Components.Metrics.Options;
using FluxFlow.Composition;
using FluxFlow.Composition.Hosting;

namespace FluxFlow.Components.Metrics.Composition;

public static class MetricsCompositionNodeRegistryExtensions
{
    public static CompositionNodeRegistry RegisterMetricsAggregate(
        this CompositionNodeRegistry registry,
        string nodeType = MetricsCompositionNodeTypes.Aggregate)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeType);

        return registry.Register(
            nodeType,
            CreateMetricsAggregateNode,
            inputs:
            [
                CompositionPorts.Metadata<MetricSampleInput>(
                    MetricsCompositionPortNames.Input)
            ],
            outputs:
            [
                CompositionPorts.Metadata<MetricSnapshotOutput>(
                    MetricsCompositionPortNames.Output)
            ]);
    }

    private static ValueTask<ComposedNode> CreateMetricsAggregateNode(
        CompositionNodeFactoryContext context)
    {
        var options = context.BindConfiguration<MetricsAggregateOptions>();
        var clock = context.GetResource<TimeProvider>(
            MetricsCompositionResourceNames.Clock);
        var node = new MetricsAggregateNode(options, clock);

        return ValueTask.FromResult(ComposedNode.Create(
            node,
            inputs:
            [
                CompositionPorts.Input<MetricSampleInput>(
                    MetricsCompositionPortNames.Input,
                    node.Input)
            ],
            outputs:
            [
                CompositionPorts.Output<MetricSnapshotOutput>(
                    MetricsCompositionPortNames.Output,
                    node.Output)
            ],
            events: node.Events,
            errors: node.Errors));
    }
}
