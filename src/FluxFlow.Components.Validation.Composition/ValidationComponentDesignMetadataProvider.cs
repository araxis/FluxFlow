using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.Validation.Options;

namespace FluxFlow.Components.Validation.Composition;

public sealed class ValidationComponentDesignMetadataProvider : IComponentDesignMetadataProvider
{
    public IReadOnlyCollection<ComponentDesignMetadata> GetMetadata()
        => [CreateJsonSchemaValidatorMetadata()];

    private static ComponentDesignMetadata CreateJsonSchemaValidatorMetadata() => new()
    {
        Type = new ComponentType(ValidationCompositionNodeTypes.JsonSchemaValidator),
        DisplayName = "JSON Schema Validator",
        Category = "Validation",
        Summary = "Validates input messages against an inline or path-based JSON schema.",
        IconKey = "shield-check",
        PreferredNodeName = "validate",
        SuggestedEditorWidth = 460,
        Options =
        [
            new OptionDesignMetadata
            {
                Name = "schema",
                Kind = OptionValueKind.Json,
                DisplayName = "Schema",
                HelperText = "Inline JSON schema compiled during composition build."
            },
            new OptionDesignMetadata
            {
                Name = "schemaPath",
                Kind = OptionValueKind.Text,
                DisplayName = "Schema Path",
                HelperText = "Path to a JSON schema file read during composition build."
            },
            new OptionDesignMetadata
            {
                Name = "schemaId",
                Kind = OptionValueKind.Text,
                DisplayName = "Schema ID",
                HelperText = "Optional schema identifier used in results and diagnostics."
            },
            new OptionDesignMetadata
            {
                Name = "inputType",
                Kind = OptionValueKind.Text,
                DisplayName = "Input Type",
                DefaultValue = JsonSchemaValidatorOptions.ObjectTypeName,
                HelperText = "Diagnostic input type metadata; CLR input type comes from the closed registration."
            },
            new OptionDesignMetadata
            {
                Name = "valueSelector",
                Kind = OptionValueKind.Text,
                DisplayName = "Value Selector",
                DefaultValue = JsonSchemaValidatorOptions.DefaultValueSelector,
                HelperText = "Selector name passed to the optional host-owned selector resource."
            },
            new OptionDesignMetadata
            {
                Name = "payloadSelector",
                Kind = OptionValueKind.Text,
                DisplayName = "Payload Selector",
                HelperText = "Compatibility alias used when valueSelector is not configured."
            },
            new OptionDesignMetadata
            {
                Name = "boundedCapacity",
                Kind = OptionValueKind.Number,
                DisplayName = "Bounded Capacity",
                DefaultValue = 128,
                Min = 1,
                HelperText = "Maximum queued input messages."
            }
        ],
        Resources =
        [
            new ResourceDesignMetadata
            {
                Name = ValidationCompositionResourceNames.Selector,
                DisplayName = "Selector",
                Order = 0,
                Summary = "Optional keyed JSON schema value selector used to choose the value to validate.",
                ValueType = "IJsonSchemaValueSelector<TInput>"
            },
            new ResourceDesignMetadata
            {
                Name = ValidationCompositionResourceNames.Clock,
                DisplayName = "Clock",
                Order = 1,
                Summary = "Optional keyed clock for deterministic validation results and diagnostics.",
                ValueType = nameof(TimeProvider)
            }
        ],
        Ports =
        [
            new PortDesignMetadata
            {
                Name = new ComponentPortName(ValidationCompositionPortNames.Input),
                Direction = PortDirection.Input,
                DisplayName = "Input",
                Group = "Messages",
                Order = 0,
                Summary = "Input message to validate.",
                ValueType = "TInput",
                IsPrimary = true
            },
            new PortDesignMetadata
            {
                Name = new ComponentPortName(ValidationCompositionPortNames.Output),
                Direction = PortDirection.Output,
                DisplayName = "Output",
                Group = "Results",
                Order = 1,
                Summary = "JSON schema validation result.",
                ValueType = "JsonSchemaValidationResult<TInput>",
                IsPrimary = true
            },
            new PortDesignMetadata
            {
                Name = new ComponentPortName(ValidationCompositionPortNames.Valid),
                Direction = PortDirection.Output,
                DisplayName = "Valid",
                Group = "Branches",
                Order = 2,
                Summary = "Original input when validation succeeds.",
                ValueType = "TInput"
            },
            new PortDesignMetadata
            {
                Name = new ComponentPortName(ValidationCompositionPortNames.Invalid),
                Direction = PortDirection.Output,
                DisplayName = "Invalid",
                Group = "Branches",
                Order = 3,
                Summary = "Original input when validation fails.",
                ValueType = "TInput"
            }
        ]
    };
}
