using FluxFlow.Nodes;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using FluxFlow.Components.State.Contracts;
using FluxFlow.Components.State.Nodes;
using FluxFlow.Components.State.Options;
using FluxFlow.Composition;
using FluxFlow.Mapping;

namespace FluxFlow.Components.State.Composition;

public static class StateServiceCollectionExtensions
{
    public static IServiceCollection AddState(this IServiceCollection builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddComponent(StateComponentDefinition.Types.Reducer, StateServiceCollectionExtensions.CreateStateReducerNode);
    }

    internal static ValueTask<IFlowNode> CreateStateReducerNode(
        ComponentActivationContext context)
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
            StateComponentDefinition.Resources.Engine);
        var clock = context.GetResource<TimeProvider>(
            StateComponentDefinition.Resources.Clock);
        var node = new JsonStateReducerNode(options, expressionEngine, clock);
        context.HasInput(node.Input);
        context.HasOutput(node.Output);
        context.HasEvents(node.Events);
        return ValueTask.FromResult<IFlowNode>(node);
    }

    private static JsonElement DecodeInitialState(JsonElement? value)
        => value?.Clone() ?? JsonSerializer.SerializeToElement<object?>(null);
}
