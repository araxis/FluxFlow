using System.Text.Json;
using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.Validation.Contracts;
using FluxFlow.Components.Validation.Nodes;
using FluxFlow.Components.Validation.Options;
using FluxFlow.Composition;

namespace FluxFlow.Components.Validation.Composition;

public static class ValidationServiceCollectionExtensions
{
    public static FluxFlowRegistrationBuilder AddValidation(this FluxFlowRegistrationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddComponent(ValidationComponentDefinition.Types.JsonSchemaValidator, ConfigureJsonSchemaValidator);
    }

    private static void ConfigureJsonSchemaValidator(ComponentRegistrationBuilder component)
    {
        component.UseFactory(CreateJsonSchemaValidatorNode);
        component.WithDisplay("JSON Schema Validator", "Validation", "Validates schema-less JSON values against an inline or path-based JSON schema.", "shield-check", "validate", 460);
        component.AddInput<JsonElement>(ValidationComponentDefinition.Ports.Input, "Input", "Messages", 0, "Immutable workflow value to validate.", true);
        component.AddOutput<JsonSchemaValidationResult>(ValidationComponentDefinition.Ports.Output, "Output", "Results", 1, "Normal valid, invalid, or processing-failure result.", true);
        component.AddOption<JsonElement?>(ValidationComponentDefinition.Options.Schema, OptionValueKind.Json, "Schema", "Inline JSON schema compiled during composition build.", section: "Schema", importance: OptionDesignMetadataAttributeValues.Primary, editor: OptionDesignMetadataAttributeValues.Json);
        component.AddOption<string>(ValidationComponentDefinition.Options.SchemaPath, OptionValueKind.Text, "Schema Path", "Path to a JSON schema file read during composition build.", section: "Schema", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Text);
        component.AddOption<string>(ValidationComponentDefinition.Options.SchemaId, OptionValueKind.Text, "Schema ID", "Optional schema identifier used in results and diagnostics.", section: "Diagnostics", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Text);
        component.AddOption<string>(ValidationComponentDefinition.Options.InputType, OptionValueKind.Text, "Input Type", "Diagnostic type metadata; the canonical input is JsonElement.", defaultValue: JsonSchemaValidatorOptions.ObjectTypeName, section: "Type Metadata", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Text);
        component.AddOption<string>(ValidationComponentDefinition.Options.ValueSelector, OptionValueKind.Text, "Value Selector", "Selector name passed to the optional host-owned selector resource.", defaultValue: JsonSchemaValidatorOptions.DefaultValueSelector, section: "Selection", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Text, relatedResource: ValidationComponentDefinition.Resources.Selector);
        component.AddOption<int>(ValidationComponentDefinition.Options.BoundedCapacity, OptionValueKind.Number, "Bounded Capacity", "Capacity used for bounded processing and reliable normal-data output.", defaultValue: 128, min: 1, section: "Runtime", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Number);
        component.AddResource<IJsonSchemaValueSelector>(ValidationComponentDefinition.Resources.Selector, "Selector", 0, "Optional keyed JSON schema value selector used to choose the value to validate.", designValueType: nameof(IJsonSchemaValueSelector), ownership: ResourceDesignMetadataAttributeValues.HostOwned, pickerKind: ResourceDesignMetadataAttributeValues.Selector, keyPattern: "Resources.{name}");
        component.AddResource<TimeProvider>(ValidationComponentDefinition.Resources.Clock, "Clock", 1, "Optional keyed clock for deterministic validation results and diagnostics.", designValueType: nameof(TimeProvider), ownership: ResourceDesignMetadataAttributeValues.HostOwned, pickerKind: ResourceDesignMetadataAttributeValues.Clock, keyPattern: "Resources.{name}");
    }

    private static ValueTask<ComponentInstance> CreateJsonSchemaValidatorNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<JsonSchemaValidatorOptions>();
        var schema = options.LoadSchema();
        var selector = context.GetResource<IJsonSchemaValueSelector>(
            ValidationComponentDefinition.Resources.Selector);
        var clock = context.GetResource<TimeProvider>(
            ValidationComponentDefinition.Resources.Clock);
        var node = new JsonSchemaValidatorNode(
            schema,
            selector,
            options.EffectiveValueSelector,
            options.SchemaId,
            options.SchemaPath,
            clock,
            options);

        return ValueTask.FromResult(ComponentInstance.Create(
            node,
            inputs:
            [
                ComponentPorts.Input<JsonElement>(
                    ValidationComponentDefinition.Ports.Input,
                    node.Input)
            ],
            outputs:
            [
                ComponentPorts.Output<JsonSchemaValidationResult>(
                    ValidationComponentDefinition.Ports.Output,
                    node.Output)
            ],
            events: node.Events));
    }
}
