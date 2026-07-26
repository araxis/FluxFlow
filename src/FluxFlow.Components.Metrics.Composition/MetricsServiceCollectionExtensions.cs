using FluxFlow.Components.Designer;
using FluxFlow.Components.Metrics.Contracts;
using FluxFlow.Components.Metrics.Nodes;
using FluxFlow.Components.Metrics.Options;
using FluxFlow.Composition;
using FluxFlow.Data;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Components.Metrics.Composition;

public static class MetricsServiceCollectionExtensions
{
    internal static ComponentDescriptor AggregateDescriptor { get; } = new(
        MetricsComponentTypes.Aggregate,
        CreateMetricsAggregateNode,
        inputs:
        [
            ComponentPorts.Metadata<MetricSampleInput>(
                MetricsComponentPortNames.Input)
        ],
        outputs:
        [
            ComponentPorts.Metadata<MetricSnapshotOutput>(
                MetricsComponentPortNames.Output)
        ],
        aliases: [MetricsComponentTypes.LegacyAggregate]);

    public static IServiceCollection AddMetricsComponents(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddFluxFlowComponent(AggregateDescriptor);
        services.AddComponentDesignMetadataProvider<MetricsComponentDesignMetadataProvider>();
        return services;
    }

    private static ValueTask<ComponentInstance> CreateMetricsAggregateNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<MetricsAggregateOptions>();
        var clock = context.GetResource<TimeProvider>(
            MetricsComponentResourceNames.Clock);
        var node = new MetricsAggregateNode(options, clock);

        return ValueTask.FromResult(ComponentInstance.Create(
            node,
            inputs:
            [
                ComponentPorts.Input<MetricSampleInput>(
                    MetricsComponentPortNames.Input,
                    node.Input)
            ],
            outputs:
            [
                ComponentPorts.Output<MetricSnapshotOutput>(
                    MetricsComponentPortNames.Output,
                    node.Output)
            ],
            events: node.Events));
    }
}
