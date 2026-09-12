using System.Text.Json;
using FluxFlow.Components.Validation.Contracts;
using FluxFlow.Components.Validation.Nodes;
using FluxFlow.Components.Validation.Options;
using FluxFlow.Composition;
using FluxFlow.Data;
using FluxFlow.Nodes;

namespace FluxFlow.Components.Validation.Composition;

public static class ValidationServiceCollectionExtensions
{
    public static FluxFlowRegistrationBuilder AddValidation(this FluxFlowRegistrationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddComponent(ValidationComponents.JsonSchemaValidator);
    }

    internal static void ConfigureJsonSchemaValidator(RuntimeComponentRegistrationBuilder component)
    {
        component.AddOption<JsonElement?>(ValidationComponentDefinition.Options.Schema);
        component.AddOption<string>(ValidationComponentDefinition.Options.SchemaPath);
        component.AddOption<string>(ValidationComponentDefinition.Options.SchemaId);
        component.AddOption<string>(ValidationComponentDefinition.Options.InputType);
        component.AddOption<string>(ValidationComponentDefinition.Options.ValueSelector);
        component.AddOption<int>(ValidationComponentDefinition.Options.BoundedCapacity);
        component.AddResource<IJsonSchemaValueSelector>(ValidationComponentDefinition.Resources.Selector);
        component.AddResource<TimeProvider>(ValidationComponentDefinition.Resources.Clock);
        component
            .UseFactory(CreateJsonSchemaValidatorNode)
            .HasStandardPorts(FlowValueMaterializers.JsonElement.InputShape);
    }

    private static JsonSchemaValidatorNode CreateJsonSchemaValidatorNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<JsonSchemaValidatorOptions>();
        var schema = options.LoadSchema();
        var selector = context.GetResource<IJsonSchemaValueSelector>(
            ValidationComponentDefinition.Resources.Selector);
        var clock = context.GetResource<TimeProvider>(
            ValidationComponentDefinition.Resources.Clock);
        return new JsonSchemaValidatorNode(
            schema,
            selector,
            options.EffectiveValueSelector,
            options.SchemaId,
            options.SchemaPath,
            clock,
            options);
    }
}
