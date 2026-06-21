using FluxFlow.Components.State.Contracts;
using FluxFlow.Components.State.Nodes;
using FluxFlow.Components.State.Options;
using FluxFlow.Composition;
using FluxFlow.Composition.Hosting;
using FluxFlow.Mapping;

namespace FluxFlow.Components.State.Composition;

public static class StateCompositionNodeRegistryExtensions
{
    public static CompositionNodeRegistry RegisterStateReducer(
        this CompositionNodeRegistry registry,
        string nodeType = StateCompositionNodeTypes.Reducer)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeType);

        return registry.Register(
            nodeType,
            CreateStateReducerNode,
            inputs:
            [
                CompositionPorts.Metadata<StateReducerInput>(
                    StateCompositionPortNames.Input)
            ],
            outputs:
            [
                CompositionPorts.Metadata<StateReducerResult>(
                    StateCompositionPortNames.Output)
            ]);
    }

    private static ValueTask<ComposedNode> CreateStateReducerNode(
        CompositionNodeFactoryContext context)
    {
        var options = context.BindConfiguration<StateReducerOptions>();
        var expressionEngine = context.GetRequiredResource<IFlowExpressionEngine>(
            StateCompositionResourceNames.Engine);
        var clock = context.GetResource<TimeProvider>(
            StateCompositionResourceNames.Clock);
        var node = new StateReducerNode(options, expressionEngine, clock);

        return ValueTask.FromResult(ComposedNode.Create(
            node,
            inputs:
            [
                CompositionPorts.Input<StateReducerInput>(
                    StateCompositionPortNames.Input,
                    node.Input)
            ],
            outputs:
            [
                CompositionPorts.Output<StateReducerResult>(
                    StateCompositionPortNames.Output,
                    node.Output)
            ],
            events: node.Events,
            errors: node.Errors));
    }
}
