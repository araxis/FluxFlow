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
    private static readonly Lazy<IReadOnlyCollection<ComponentDesignDeclaration>> DeclarationSet =
        new(CreateDeclarations);

    internal static IReadOnlyCollection<ComponentDesignDeclaration> Declarations =>
        DeclarationSet.Value;

    private static IReadOnlyCollection<ComponentDesignDeclaration> CreateDeclarations() =>
        ComponentDesignDeclaration.CreateRange(
            [
                ReducerDescriptor
            ],
            StateComponentDefinition.CreateMetadata());

    internal static ComponentDescriptor ReducerDescriptor { get; } = new(
        StateComponentDefinition.Types.Reducer,
        CreateStateReducerNode,
        inputs:
        [
            ComponentPorts.Metadata<StateReducerInput<JsonElement>>(
                StateComponentDefinition.Ports.Input)
        ],
        outputs:
        [
            ComponentPorts.Metadata<StateReducerResult<JsonElement>>(
                StateComponentDefinition.Ports.Output)
        ],
        options: StateComponentDefinition.CreateOptions(StateComponentDefinition.Types.Reducer),
        resources: StateComponentDefinition.CreateResources(StateComponentDefinition.Types.Reducer));

    public static IServiceCollection AddStateComponents(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddComponentDesignDeclarations(Declarations);
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
            StateComponentDefinition.Resources.Engine);
        var clock = context.GetResource<TimeProvider>(
            StateComponentDefinition.Resources.Clock);
        var node = new JsonStateReducerNode(options, expressionEngine, clock);

        return ValueTask.FromResult(ComponentInstance.Create(
            node,
            inputs:
            [
                ComponentPorts.Input<StateReducerInput<JsonElement>>(
                    StateComponentDefinition.Ports.Input,
                    node.Input)
            ],
            outputs:
            [
                ComponentPorts.Output<StateReducerResult<JsonElement>>(
                    StateComponentDefinition.Ports.Output,
                    node.Output)
            ],
            events: node.Events));
    }

    private static JsonElement DecodeInitialState(JsonElement? value)
        => value?.Clone() ?? JsonSerializer.SerializeToElement<object?>(null);
}
