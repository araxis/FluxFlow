using FluxFlow.Nodes;
using Microsoft.Extensions.DependencyInjection;
using FluxFlow.Components.Metrics.Contracts;
using FluxFlow.Components.Metrics.Nodes;
using FluxFlow.Components.Metrics.Options;
using FluxFlow.Composition;
using FluxFlow.Data;

namespace FluxFlow.Components.Metrics.Composition;

public static class MetricsServiceCollectionExtensions
{
    public static IServiceCollection AddFluxFlowMetrics(this IServiceCollection builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddComponent(MetricsComponentDefinition.Types.Aggregate, MetricsServiceCollectionExtensions.CreateMetricsAggregateNode);
    }

    internal static ValueTask<IFlowNode> CreateMetricsAggregateNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<MetricsAggregateOptions>();
        var clock = context.GetResource<TimeProvider>(
            MetricsComponentDefinition.Resources.Clock);
        var node = new MetricsAggregateNode(options, clock);
        context.HasInput(node.Input);
        context.HasOutput(node.Output);
        context.HasEvents(node.Events);
        return ValueTask.FromResult<IFlowNode>(node);
    }
}
