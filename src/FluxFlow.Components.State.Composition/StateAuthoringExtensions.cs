using System.Text.Json;
using FluxFlow.Components.State.Contracts;
using FluxFlow.Components.Designer;
using FluxFlow.Composition.Authoring;
using FluxFlow.Mapping;

namespace FluxFlow.Components.State.Composition;

public static class StateComponents
{
    public static ComponentContract<StateReducerComponentBuilder, InputOutputComponentHandle<StateReducerInput<JsonElement>, StateReducerResult<JsonElement>>> StateReducer { get; } =
        DesignedComponentContract.Create(
            StateComponentDefinition.Types.Reducer,
            StateServiceCollectionExtensions.ConfigureReducer,
            static () => new StateReducerComponentBuilder(),
            static (options, definition) => options.Apply(definition),
            static component => new InputOutputComponentHandle<StateReducerInput<JsonElement>, StateReducerResult<JsonElement>>(component, StateComponentDefinition.Ports.Input, StateComponentDefinition.Ports.Output, StateComponentDefinition.Ports.Events));
}

public static class StateAuthoringExtensions
{
    public static InputOutputComponentHandle<StateReducerInput<JsonElement>, StateReducerResult<JsonElement>> AddStateReducer(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<StateReducerComponentBuilder> configure)
        => workflow.AddComponent(name, StateComponents.StateReducer, configure);

    public static WorkflowDefinitionBuilder AddStateReducer(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<StateReducerComponentBuilder> configure,
        out InputOutputComponentHandle<StateReducerInput<JsonElement>, StateReducerResult<JsonElement>> reducer)
    {
        reducer = workflow.AddStateReducer(name, configure);
        return workflow;
    }
}

public sealed class StateReducerComponentBuilder
{
    public string? KeyExpression { get; set; }
    public string? Reducer { get; set; }
    public string? ExpressionId { get; set; }
    public string? ExpressionName { get; set; }
    public JsonElement? InitialState { get; set; }
    public int? BoundedCapacity { get; set; }
    public int? MaxKeys { get; set; }
    public ResourceHandle<IFlowExpressionEngine>? Engine { get; set; }
    public ResourceHandle<TimeProvider>? Clock { get; set; }

    internal void Apply(ComponentDefinitionBuilder definition)
    {
        if (string.IsNullOrWhiteSpace(Reducer))
            throw new InvalidOperationException("State reducer components require Reducer.");
        if (Engine is null)
            throw new InvalidOperationException("State reducer components require Engine.");

        Set(definition, StateComponentDefinition.Options.KeyExpression, KeyExpression);
        definition.Set(StateComponentDefinition.Options.Reducer, Reducer);
        Set(definition, StateComponentDefinition.Options.ExpressionId, ExpressionId);
        Set(definition, StateComponentDefinition.Options.ExpressionName, ExpressionName);
        Set(definition, StateComponentDefinition.Options.InitialState, InitialState);
        Set(definition, StateComponentDefinition.Options.BoundedCapacity, BoundedCapacity);
        Set(definition, StateComponentDefinition.Options.MaxKeys, MaxKeys);
        definition.UseResource(StateComponentDefinition.Resources.Engine, Engine);
        if (Clock is not null)
            definition.UseResource(StateComponentDefinition.Resources.Clock, Clock);
    }

    private static void Set<T>(ComponentDefinitionBuilder definition, string name, T? value)
    {
        if (value is not null)
            definition.Set(name, value);
    }
}
