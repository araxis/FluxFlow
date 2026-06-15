using FluxFlow.Components.Expectations.Options;
using FluxFlow.Engine.Runtime;

namespace FluxFlow.Components.Expectations.Nodes;

internal static class EventExpectationNodeFactory
{
    public static RuntimeNode Create(
        RuntimeNodeFactoryContext context,
        ExpectationsComponentOptions componentOptions,
        EventExpectationNodeKind kind)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(componentOptions);

        var settings = ExpectationsOptionsReader.ReadEventExpectationSettings(context.Definition);
        var node = new EventExpectationNode(settings, componentOptions.Clock, kind);

        return context.CreateNode(node)
            .Input(ExpectationsComponentPorts.Input, node.Input)
            .Output(ExpectationsComponentPorts.Result, node.Result)
            .Output(ExpectationsComponentPorts.Errors, node.Errors)
            .Build();
    }
}
