using System.Text.Json;
using FluxFlow.Components.Validation.Contracts;
using FluxFlow.Components.Designer;
using FluxFlow.Composition.Authoring;

namespace FluxFlow.Components.Validation.Composition;

public static class ValidationComponents
{
    public static ComponentContract<JsonSchemaValidatorComponentBuilder, InputOutputComponentHandle<JsonElement, JsonSchemaValidationResult>> JsonSchemaValidator { get; } =
        DesignedComponentContract.Create(
            ValidationComponentDefinition.Types.JsonSchemaValidator,
            ValidationServiceCollectionExtensions.ConfigureJsonSchemaValidator,
            static () => new JsonSchemaValidatorComponentBuilder(),
            static (options, definition) => options.Apply(definition),
            static component => new InputOutputComponentHandle<JsonElement, JsonSchemaValidationResult>(component, ValidationComponentDefinition.Ports.Input, ValidationComponentDefinition.Ports.Output, ValidationComponentDefinition.Ports.Events));
}

public static class ValidationAuthoringExtensions
{
    public static InputOutputComponentHandle<JsonElement, JsonSchemaValidationResult> AddJsonSchemaValidator(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<JsonSchemaValidatorComponentBuilder>? configure = null)
        => workflow.AddComponent(
            name,
            ValidationComponents.JsonSchemaValidator,
            configure ?? (static _ => { }));

    public static WorkflowDefinitionBuilder AddJsonSchemaValidator(
        this WorkflowDefinitionBuilder workflow,
        string name,
        out InputOutputComponentHandle<JsonElement, JsonSchemaValidationResult> validator)
    {
        validator = workflow.AddJsonSchemaValidator(name);
        return workflow;
    }

    public static WorkflowDefinitionBuilder AddJsonSchemaValidator(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<JsonSchemaValidatorComponentBuilder> configure,
        out InputOutputComponentHandle<JsonElement, JsonSchemaValidationResult> validator)
    {
        ArgumentNullException.ThrowIfNull(configure);
        validator = workflow.AddJsonSchemaValidator(name, configure);
        return workflow;
    }
}

public sealed class JsonSchemaValidatorComponentBuilder
{
    public JsonElement? Schema { get; set; }
    public string? SchemaPath { get; set; }
    public string? SchemaId { get; set; }
    public string? InputType { get; set; }
    public string? ValueSelector { get; set; }
    public int? BoundedCapacity { get; set; }
    public ResourceHandle<IJsonSchemaValueSelector>? Selector { get; set; }
    public ResourceHandle<TimeProvider>? Clock { get; set; }

    internal void Apply(ComponentDefinitionBuilder definition)
    {
        Set(definition, ValidationComponentDefinition.Options.Schema, Schema);
        Set(definition, ValidationComponentDefinition.Options.SchemaPath, SchemaPath);
        Set(definition, ValidationComponentDefinition.Options.SchemaId, SchemaId);
        Set(definition, ValidationComponentDefinition.Options.InputType, InputType);
        Set(definition, ValidationComponentDefinition.Options.ValueSelector, ValueSelector);
        Set(definition, ValidationComponentDefinition.Options.BoundedCapacity, BoundedCapacity);
        Use(definition, ValidationComponentDefinition.Resources.Selector, Selector);
        Use(definition, ValidationComponentDefinition.Resources.Clock, Clock);
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
