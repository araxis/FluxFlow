using System.Text.Json;
using FluxFlow.Components.Assertions.Contracts;
using FluxFlow.Components.Designer;
using FluxFlow.Composition.Authoring;
using FluxFlow.Mapping;

namespace FluxFlow.Components.Assertions.Composition;

public static class AssertionsComponents
{
    public static ComponentContract<AssertionComponentBuilder, InputOutputComponentHandle<JsonElement, AssertionResult<JsonElement>>> Assertion { get; } =
        DesignedComponentContract.Create(
            AssertionsComponentDefinition.Types.Assertion,
            AssertionsServiceCollectionExtensions.ConfigureAssertion,
            static () => new AssertionComponentBuilder(),
            static (options, definition) => options.Apply(definition),
            static component => new InputOutputComponentHandle<JsonElement, AssertionResult<JsonElement>>(component, AssertionsComponentDefinition.Ports.Input, AssertionsComponentDefinition.Ports.Output, AssertionsComponentDefinition.Ports.Events));
}

public static class AssertionsAuthoringExtensions
{
    public static InputOutputComponentHandle<JsonElement, AssertionResult<JsonElement>> AddAssertion(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<AssertionComponentBuilder> configure)
        => workflow.AddComponent(name, AssertionsComponents.Assertion, configure);

    public static WorkflowDefinitionBuilder AddAssertion(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<AssertionComponentBuilder> configure,
        out InputOutputComponentHandle<JsonElement, AssertionResult<JsonElement>> assertion)
    {
        assertion = workflow.AddAssertion(name, configure);
        return workflow;
    }
}

public sealed class AssertionComponentBuilder
{
    public string? Expression { get; set; }
    public string? ExpressionId { get; set; }
    public string? ExpressionName { get; set; }
    public string? InputType { get; set; }
    public int? BoundedCapacity { get; set; }
    public string? Description { get; set; }
    public string? FailureMessage { get; set; }
    public ResourceHandle<IFlowExpressionEngine>? Engine { get; set; }
    public ResourceHandle<IFlowMapContextFactory<JsonElement>>? ContextFactory { get; set; }
    public ResourceHandle<TimeProvider>? Clock { get; set; }

    internal void Apply(ComponentDefinitionBuilder definition)
    {
        if (string.IsNullOrWhiteSpace(Expression))
            throw new InvalidOperationException("Assertion components require Expression.");
        if (Engine is null)
            throw new InvalidOperationException("Assertion components require Engine.");

        definition.Set(AssertionsComponentDefinition.Options.Expression, Expression);
        Set(definition, AssertionsComponentDefinition.Options.ExpressionId, ExpressionId);
        Set(definition, AssertionsComponentDefinition.Options.ExpressionName, ExpressionName);
        Set(definition, AssertionsComponentDefinition.Options.InputType, InputType);
        Set(definition, AssertionsComponentDefinition.Options.BoundedCapacity, BoundedCapacity);
        Set(definition, AssertionsComponentDefinition.Options.Description, Description);
        Set(definition, AssertionsComponentDefinition.Options.FailureMessage, FailureMessage);
        definition.UseResource(AssertionsComponentDefinition.Resources.Engine, Engine);
        Use(definition, AssertionsComponentDefinition.Resources.ContextFactory, ContextFactory);
        Use(definition, AssertionsComponentDefinition.Resources.Clock, Clock);
    }

    private static void Set<T>(ComponentDefinitionBuilder definition, string name, T? value)
    {
        if (value is not null)
            definition.Set(name, value);
    }

    private static void Use(ComponentDefinitionBuilder definition, string name, ResourceHandle? value)
    {
        if (value is not null)
            definition.UseResource(name, value);
    }
}
