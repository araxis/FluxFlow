using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
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
                summary: "Validates input messages against an inline or path-based JSON schema.",
                iconKey: "shield-check",
                preferredNodeName: "validate",
                suggestedEditorWidth: 460)
            .AddOption(
                "schema",
                OptionValueKind.Json,
                displayName: "Schema",
                helperText: "Inline JSON schema compiled during composition build.")
            .AddOption(
                "schemaPath",
                OptionValueKind.Text,
                displayName: "Schema Path",
                helperText: "Path to a JSON schema file read during composition build.")
            .AddOption(
                "schemaId",
                OptionValueKind.Text,
                displayName: "Schema ID",
                helperText: "Optional schema identifier used in results and diagnostics.")
            .AddOption(
                "inputType",
                OptionValueKind.Text,
                displayName: "Input Type",
                defaultValue: JsonSchemaValidatorOptions.ObjectTypeName,
                helperText: "Diagnostic input type metadata; CLR input type comes from the closed registration.")
            .AddOption(
                "valueSelector",
                OptionValueKind.Text,
                displayName: "Value Selector",
                defaultValue: JsonSchemaValidatorOptions.DefaultValueSelector,
                helperText: "Selector name passed to the optional host-owned selector resource.")
            .AddOption(
                "payloadSelector",
                OptionValueKind.Text,
                displayName: "Payload Selector",
                helperText: "Compatibility alias used when valueSelector is not configured.")
            .AddOption(
                "boundedCapacity",
                OptionValueKind.Number,
                displayName: "Bounded Capacity",
                helperText: "Maximum queued input messages.",
                defaultValue: 128,
                min: 1)
            .AddResource(
                ValidationCompositionResourceNames.Selector,
                displayName: "Selector",
                order: 0,
                summary: "Optional keyed JSON schema value selector used to choose the value to validate.",
                valueType: "IJsonSchemaValueSelector<TInput>")
            .AddResource(
                ValidationCompositionResourceNames.Clock,
                displayName: "Clock",
                order: 1,
                summary: "Optional keyed clock for deterministic validation results and diagnostics.",
                valueType: nameof(TimeProvider))
            .AddInputPort(
                ValidationCompositionPortNames.Input,
                displayName: "Input",
                group: "Messages",
                order: 0,
                summary: "Input message to validate.",
                valueType: "TInput",
                isPrimary: true)
            .AddOutputPort(
                ValidationCompositionPortNames.Output,
                displayName: "Output",
                group: "Results",
                order: 1,
                summary: "JSON schema validation result.",
                valueType: "JsonSchemaValidationResult<TInput>",
                isPrimary: true)
            .AddOutputPort(
                ValidationCompositionPortNames.Valid,
                displayName: "Valid",
                group: "Branches",
                order: 2,
                summary: "Original input when validation succeeds.",
                valueType: "TInput")
            .AddOutputPort(
                ValidationCompositionPortNames.Invalid,
                displayName: "Invalid",
                group: "Branches",
                order: 3,
                summary: "Original input when validation fails.",
                valueType: "TInput")
            .Build();
}
