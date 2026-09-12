using System.Text.Json;
using FluxFlow.Components.State.Contracts;
using FluxFlow.Components.State.Nodes;
using FluxFlow.Components.State.Options;
using FluxFlow.Composition;
using FluxFlow.Mapping;

namespace FluxFlow.Components.State.Composition;

public static class StateServiceCollectionExtensions
{
    public static FluxFlowRegistrationBuilder AddState(this FluxFlowRegistrationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddComponent(StateComponents.StateReducer);
    }

    internal static void ConfigureReducer(RuntimeComponentRegistrationBuilder component)
    {
        component.AddOption<string>(StateComponentDefinition.Options.KeyExpression);
        component.AddOption<string>(StateComponentDefinition.Options.Reducer, isRequired: true);
        component.AddOption<string>(StateComponentDefinition.Options.ExpressionId);
        component.AddOption<string>(StateComponentDefinition.Options.ExpressionName);
        component.AddOption<JsonElement?>(StateComponentDefinition.Options.InitialState);
        component.AddOption<int>(StateComponentDefinition.Options.BoundedCapacity);
        component.AddOption<int>(StateComponentDefinition.Options.MaxKeys);
        component.AddResource<IFlowExpressionEngine>(StateComponentDefinition.Resources.Engine, isRequired: true);
        component.AddResource<TimeProvider>(StateComponentDefinition.Resources.Clock);
        component
            .UseFactory(CreateStateReducerNode)
            .HasStandardPorts(new FluxFlow.Components.State.Materialization.StateReducerInputMaterializer().InputShape);
    }

    private static JsonStateReducerNode CreateStateReducerNode(
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
        return new JsonStateReducerNode(options, expressionEngine, clock);
    }

    private static JsonElement DecodeInitialState(JsonElement? value)
        => value?.Clone() ?? JsonSerializer.SerializeToElement<object?>(null);
}
