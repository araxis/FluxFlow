using System.Text.Json;
using FluxFlow.Components.Designer;
using FluxFlow.Components.State.Contracts;
using FluxFlow.Components.State.Nodes;
using FluxFlow.Components.State.Options;
using FluxFlow.Composition;
using FluxFlow.Mapping;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Components.State.Composition;

public static class StateServiceCollectionExtensions
{
    internal static ComponentDescriptor ReducerDescriptor { get; } = new(
        StateComponentTypes.Reducer,
        CreateStateReducerNode,
        inputs:
        [
            ComponentPorts.Metadata<StateReducerInput<JsonElement>>(
                StateComponentPortNames.Input)
        ],
        outputs:
        [
            ComponentPorts.Metadata<StateReducerResult<JsonElement>>(
                StateComponentPortNames.Output)
        ]);

    public static IServiceCollection AddStateComponents(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddFluxFlowComponent(ReducerDescriptor);
        services.AddComponentDesignMetadataProvider<StateComponentDesignMetadataProvider>();
        return services;
    }

    private static ValueTask<ComponentInstance> CreateStateReducerNode(
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
            StateComponentResourceNames.Engine);
        var clock = context.GetResource<TimeProvider>(
            StateComponentResourceNames.Clock);
        var node = new JsonStateReducerNode(options, expressionEngine, clock);

        return ValueTask.FromResult(ComponentInstance.Create(
            node,
            inputs:
            [
                ComponentPorts.Input<StateReducerInput<JsonElement>>(
                    StateComponentPortNames.Input,
                    node.Input)
            ],
            outputs:
            [
                ComponentPorts.Output<StateReducerResult<JsonElement>>(
                    StateComponentPortNames.Output,
                    node.Output)
            ],
            events: node.Events));
    }

    private static JsonElement DecodeInitialState(JsonElement? value)
        => value?.Clone() ?? JsonSerializer.SerializeToElement<object?>(null);
}
