using System.Text.Json;
using FluxFlow.Components.State.Contracts;
using FluxFlow.Components.State.Nodes;
using FluxFlow.Components.State.Options;
using FluxFlow.Composition;
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
            StateCompositionNodeTypes.ReducerDescriptor,
            CreateStateReducerNode,
            inputs:
            [
                CompositionPorts.Metadata<StateReducerInput<JsonElement>>(
                    StateCompositionPortNames.Input)
            ],
            outputs:
            [
                CompositionPorts.Metadata<StateReducerResult<JsonElement>>(
                    StateCompositionPortNames.Output)
            ],
            registrationType: nodeType);
    }

    private static ValueTask<ComposedNode> CreateStateReducerNode(
        CompositionNodeFactoryContext context)
    {
        var configuration = context.BindConfiguration<StateReducerConfiguration>();
        var options = new StateReducerOptions<JsonElement>
        {
            KeyExpression = configuration.KeyExpression,
            Reducer = configuration.Reducer,
            ExpressionId = configuration.ExpressionId,
            ExpressionName = configuration.ExpressionName,
            InitialState = DecodeInitialState(configuration.InitialState),
            BoundedCapacity = configuration.BoundedCapacity,
            MaxKeys = configuration.MaxKeys
        };
        var expressionEngine = context.GetRequiredResource<IFlowExpressionEngine>(
            StateCompositionResourceNames.Engine);
        var clock = context.GetResource<TimeProvider>(
            StateCompositionResourceNames.Clock);
        var node = new JsonStateReducerNode(options, expressionEngine, clock);

        return ValueTask.FromResult(ComposedNode.Create(
            node,
            inputs:
            [
                CompositionPorts.Input<StateReducerInput<JsonElement>>(
                    StateCompositionPortNames.Input,
                    node.Input)
            ],
            outputs:
            [
                CompositionPorts.Output<StateReducerResult<JsonElement>>(
                    StateCompositionPortNames.Output,
                    node.Output)
            ],
            events: node.Events));
    }

    private static JsonElement DecodeInitialState(JsonElement? value)
        => value?.Clone() ?? JsonSerializer.SerializeToElement<object?>(null);
}
