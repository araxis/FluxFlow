using FluxFlow.Components.Metrics.Options;
using FluxFlow.Engine.Runtime;

namespace FluxFlow.Components.Metrics.Nodes;

internal static class MetricsAggregateNodeFactory
{
    public static RuntimeNode Create(RuntimeNodeFactoryContext context)
        => Create(context, new MetricsComponentOptions());

    public static RuntimeNode Create(
        RuntimeNodeFactoryContext context,
        MetricsComponentOptions componentOptions)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(componentOptions);

        var options = MetricsOptionsReader.ReadAggregateOptions(context.Definition);
        var node = new MetricsAggregateNode(options, componentOptions.Clock);

        return context.CreateNode(node)
            .Input(MetricsComponentPorts.Input, node.Input)
            .Output(MetricsComponentPorts.Output, node.Output)
            .Output(MetricsComponentPorts.Errors, node.Errors)
            .Build();
    }
}
