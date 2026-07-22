using System.Collections.Immutable;
using System.Text.Json;
using FluxFlow.Components.State.Contracts;
using FluxFlow.Components.State.Nodes;
using FluxFlow.Components.State.Options;
using FluxFlow.Composition;
using FluxFlow.Data;
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

        var result = registry.Register(
            nodeType,
            CreateStateReducerNode,
            inputs:
            [
                CompositionPorts.Metadata<FlowValueStateReducerInput>(
                    StateCompositionPortNames.Input)
            ],
            outputs:
            [
                CompositionPorts.Metadata<FlowResult<FlowValueStateReducerResult>>(
                    StateCompositionPortNames.Output)
            ]);

        if (string.Equals(nodeType, StateCompositionNodeTypes.Reducer, StringComparison.Ordinal))
        {
            result.RegisterAlias(
                StateCompositionNodeTypes.LegacyReducer,
                StateCompositionNodeTypes.Reducer);
        }

        return result;
    }

    private static ValueTask<ComposedNode> CreateStateReducerNode(
        CompositionNodeFactoryContext context)
    {
        var configuration = context.BindConfiguration<StateReducerConfiguration>();
        var options = new FlowValueStateReducerOptions
        {
            Engine = configuration.Engine,
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
        var node = new FlowValueStateReducerNode(options, expressionEngine, clock);

        return ValueTask.FromResult(ComposedNode.Create(
            node,
            inputs:
            [
                CompositionPorts.Input<FlowValueStateReducerInput>(
                    StateCompositionPortNames.Input,
                    node.Input)
            ],
            outputs:
            [
                CompositionPorts.Output<FlowResult<FlowValueStateReducerResult>>(
                    StateCompositionPortNames.Output,
                    node.Output)
            ],
            events: node.Events));
    }

    private static FlowValue DecodeInitialState(JsonElement? value)
    {
        if (!value.HasValue)
            return FlowValue.Null;

        var bytes = ImmutableArray.CreateRange(
            JsonSerializer.SerializeToUtf8Bytes(value.Value));
        return new JsonFlowContentCodec().Decode(bytes, encoding: null);
    }

}
