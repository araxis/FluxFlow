using FluxFlow.Components.Projections.Options;
using FluxFlow.Engine.Runtime;

namespace FluxFlow.Components.Projections.Nodes;

internal static class EventProjectionNodeFactory
{
    public static RuntimeNode Create(
        RuntimeNodeFactoryContext context,
        ProjectionsComponentOptions componentOptions)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(componentOptions);

        var options = ProjectionsOptionsReader.ReadEventProjectionOptions(context.Definition);
        var node = new EventProjectionNode(options, componentOptions.Clock);

        return context.CreateNode(node)
            .Input(ProjectionsComponentPorts.Input, node.Input)
            .Output(ProjectionsComponentPorts.Output, node.Output)
            .Output(ProjectionsComponentPorts.Errors, node.Errors)
            .Build();
    }
}
