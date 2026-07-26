using System.Text.Json;
using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.Validation.Contracts;
using FluxFlow.Components.Validation.Options;

namespace FluxFlow.Components.Validation.Composition;

public sealed class ValidationComponentDesignMetadataProvider : IComponentDesignMetadataProvider
{
    public IReadOnlyCollection<ComponentDesignMetadata> GetMetadata()
        => [CreateJsonSchemaValidatorMetadata()];

    private static ComponentDesignMetadata CreateJsonSchemaValidatorMetadata()
        => new ComponentDesignMetadataBuilder(ValidationCompositionNodeTypes.JsonSchemaValidator)
            .WithDisplay(
                displayName: "JSON Schema Validator",
                category: "Validation",
                summary: "Validates schema-less JSON values against an inline or path-based JSON schema.",
                iconKey: "shield-check",
                preferredNodeName: "validate",
                suggestedEditorWidth: 460)
            .AddAttribute(ComponentDesignMetadataAttributeNames.Aliases, string.Join(',', ValidationCompositionNodeTypes.JsonSchemaValidatorDescriptor.Aliases))
            .AddOption(
                "schema",
                OptionValueKind.Json,
                displayName: "Schema",
                helperText: "Inline JSON schema compiled during composition build.",
                attributes: OptionDesignMetadataAttributes.Create(
                    section: "Schema",
                    importance: OptionDesignMetadataAttributeValues.Primary,
                    editor: OptionDesignMetadataAttributeValues.Json))
            .AddOption(
                "schemaPath",
                OptionValueKind.Text,
                displayName: "Schema Path",
                helperText: "Path to a JSON schema file read during composition build.",
                attributes: OptionDesignMetadataAttributes.Create(
                    section: "Schema",
                    importance: OptionDesignMetadataAttributeValues.Advanced,
                    editor: OptionDesignMetadataAttributeValues.Text))
            .AddOption(
                "schemaId",
                OptionValueKind.Text,
                displayName: "Schema ID",
                helperText: "Optional schema identifier used in results and diagnostics.",
                attributes: OptionDesignMetadataAttributes.Create(
                    section: "Diagnostics",
                    importance: OptionDesignMetadataAttributeValues.Advanced,
                    editor: OptionDesignMetadataAttributeValues.Text))
            .AddOption(
                "inputType",
                OptionValueKind.Text,
                displayName: "Input Type",
                defaultValue: JsonSchemaValidatorOptions.ObjectTypeName,
                helperText: "Diagnostic type metadata; the canonical input is JsonElement.",
                attributes: OptionDesignMetadataAttributes.Create(
                    section: "Type Metadata",
                    importance: OptionDesignMetadataAttributeValues.Advanced,
                    editor: OptionDesignMetadataAttributeValues.Text))
            .AddOption(
                "valueSelector",
                OptionValueKind.Text,
                displayName: "Value Selector",
                defaultValue: JsonSchemaValidatorOptions.DefaultValueSelector,
                helperText: "Selector name passed to the optional host-owned selector resource.",
                attributes: OptionDesignMetadataAttributes.Create(
                    section: "Selection",
                    importance: OptionDesignMetadataAttributeValues.Advanced,
                    editor: OptionDesignMetadataAttributeValues.Text,
                    relatedResource: ValidationCompositionResourceNames.Selector))
            .AddOption(
                "boundedCapacity",
                OptionValueKind.Number,
                displayName: "Bounded Capacity",
                helperText: "Maximum queued input messages.",
                defaultValue: 128,
                min: 1,
                attributes: OptionDesignMetadataAttributes.Create(
                    section: "Runtime",
                    importance: OptionDesignMetadataAttributeValues.Advanced,
                    editor: OptionDesignMetadataAttributeValues.Number))
            .AddResource(
                ValidationCompositionResourceNames.Selector,
                displayName: "Selector",
                order: 0,
                summary: "Optional keyed JSON schema value selector used to choose the value to validate.",
                valueType: nameof(IJsonSchemaValueSelector),
                attributes: ResourceDesignMetadataAttributes.CreateHostOwned(
                    ResourceDesignMetadataAttributeValues.Selector,
                    keyPattern: "Resources.{name}"))
            .AddResource(
                ValidationCompositionResourceNames.Clock,
                displayName: "Clock",
                order: 1,
                summary: "Optional keyed clock for deterministic validation results and diagnostics.",
                valueType: nameof(TimeProvider),
                attributes: ResourceDesignMetadataAttributes.CreateHostOwned(
                    ResourceDesignMetadataAttributeValues.Clock,
                    keyPattern: "Resources.{name}"))
            .AddInputPort(
                ValidationCompositionPortNames.Input,
                displayName: "Input",
                group: "Messages",
                order: 0,
                summary: "Immutable workflow value to validate.",
                valueType: nameof(JsonElement),
                isPrimary: true)
            .AddOutputPort(
                ValidationCompositionPortNames.Output,
                displayName: "Output",
                group: "Results",
                order: 1,
                summary: "Normal valid, invalid, or processing-failure result.",
                valueType: nameof(JsonSchemaValidationResult),
                isPrimary: true)
            .Build();
}
