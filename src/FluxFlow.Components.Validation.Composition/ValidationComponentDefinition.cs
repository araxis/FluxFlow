using System.Text.Json;
using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Composition;
using FluxFlow.Components.Validation.Contracts;
using FluxFlow.Components.Validation.Options;

namespace FluxFlow.Components.Validation.Composition;

public static partial class ValidationComponentDefinition
{
    public static IReadOnlyCollection<ComponentDesignMetadata> CreateMetadata()
        => [CreateJsonSchemaValidatorMetadata()];

    private static ComponentDesignMetadata CreateJsonSchemaValidatorMetadata()
        => new ComponentDesignMetadataBuilder(ValidationComponentDefinition.Types.JsonSchemaValidator)
            .WithDisplay(
                displayName: "JSON Schema Validator",
                category: "Validation",
                summary: "Validates schema-less JSON values against an inline or path-based JSON schema.",
                iconKey: "shield-check",
                preferredNodeName: "validate",
                suggestedEditorWidth: 460)
            .AddOption(
                Options.Schema,
                OptionValueKind.Json,
                displayName: "Schema",
                helperText: "Inline JSON schema compiled during composition build.",
                attributes: OptionDesignMetadataAttributes.Create(
                    section: "Schema",
                    importance: OptionDesignMetadataAttributeValues.Primary,
                    editor: OptionDesignMetadataAttributeValues.Json))
            .AddOption(
                Options.SchemaPath,
                OptionValueKind.Text,
                displayName: "Schema Path",
                helperText: "Path to a JSON schema file read during composition build.",
                attributes: OptionDesignMetadataAttributes.Create(
                    section: "Schema",
                    importance: OptionDesignMetadataAttributeValues.Advanced,
                    editor: OptionDesignMetadataAttributeValues.Text))
            .AddOption(
                Options.SchemaId,
                OptionValueKind.Text,
                displayName: "Schema ID",
                helperText: "Optional schema identifier used in results and diagnostics.",
                attributes: OptionDesignMetadataAttributes.Create(
                    section: "Diagnostics",
                    importance: OptionDesignMetadataAttributeValues.Advanced,
                    editor: OptionDesignMetadataAttributeValues.Text))
            .AddOption(
                Options.InputType,
                OptionValueKind.Text,
                displayName: "Input Type",
                defaultValue: JsonSchemaValidatorOptions.ObjectTypeName,
                helperText: "Diagnostic type metadata; the canonical input is JsonElement.",
                attributes: OptionDesignMetadataAttributes.Create(
                    section: "Type Metadata",
                    importance: OptionDesignMetadataAttributeValues.Advanced,
                    editor: OptionDesignMetadataAttributeValues.Text))
            .AddOption(
                Options.ValueSelector,
                OptionValueKind.Text,
                displayName: "Value Selector",
                defaultValue: JsonSchemaValidatorOptions.DefaultValueSelector,
                helperText: "Selector name passed to the optional host-owned selector resource.",
                attributes: OptionDesignMetadataAttributes.Create(
                    section: "Selection",
                    importance: OptionDesignMetadataAttributeValues.Advanced,
                    editor: OptionDesignMetadataAttributeValues.Text,
                    relatedResource: ValidationComponentDefinition.Resources.Selector))
            .AddOption(OptionDesignMetadataFactory.BoundedCapacity(128))
            .AddResource(ResourceDesignMetadataFactory.HostOwned(
                ValidationComponentDefinition.Resources.Selector,
                ResourceDesignMetadataAttributeValues.Selector,
                "Selector",
                0,
                "Optional keyed JSON schema value selector used to choose the value to validate.",
                nameof(IJsonSchemaValueSelector),
                keyPattern: "Resources.{name}"))
            .AddResource(ResourceDesignMetadataFactory.HostOwned(
                ValidationComponentDefinition.Resources.Clock,
                ResourceDesignMetadataAttributeValues.Clock,
                "Clock",
                1,
                "Optional keyed clock for deterministic validation results and diagnostics.",
                nameof(TimeProvider),
                keyPattern: "Resources.{name}"))
            .AddInputPort(
                ValidationComponentDefinition.Ports.Input,
                displayName: Ports.Input,
                group: "Messages",
                order: 0,
                summary: "Immutable workflow value to validate.",
                valueType: nameof(JsonElement),
                isPrimary: true)
            .AddOutputPort(
                ValidationComponentDefinition.Ports.Output,
                displayName: Ports.Output,
                group: "Results",
                order: 1,
                summary: "Normal valid, invalid, or processing-failure result.",
                valueType: nameof(JsonSchemaValidationResult),
                isPrimary: true)
            .Build();


    public static class Options
    {
        public const string Schema = "schema";
        public const string SchemaPath = "schemaPath";
        public const string SchemaId = "schemaId";
        public const string InputType = "inputType";
        public const string ValueSelector = "valueSelector";
        public const string BoundedCapacity = "boundedCapacity";
    }

    internal static IReadOnlyCollection<ComponentOptionMetadata> CreateOptions(string type)
        => type switch
        {
            Types.JsonSchemaValidator =>
            [
                ComponentOptions.Metadata<JsonElement?>(Options.Schema),
                ComponentOptions.Metadata<string>(Options.SchemaPath),
                ComponentOptions.Metadata<string>(Options.SchemaId),
                ComponentOptions.Metadata<string>(Options.InputType),
                ComponentOptions.Metadata<string>(Options.ValueSelector),
                ComponentOptions.Metadata<int>(Options.BoundedCapacity)
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown component type.")
        };

    internal static IReadOnlyCollection<ComponentResourceMetadata> CreateResources(string type)
        => type switch
        {
            Types.JsonSchemaValidator =>
            [
                ComponentResources.Metadata<IJsonSchemaValueSelector>(Resources.Selector),
                ComponentResources.Metadata<TimeProvider>(Resources.Clock)
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown component type.")
        };

    public static class Types
    {
        public const string JsonSchemaValidator = "json.validate";
    }

    public static class Ports
    {
        public const string Input = "Input";
    
        public const string Output = "Output";
    }

    public static class Resources
    {
        public const string Selector = "selector";
    
        public const string Clock = "clock";
    }
}
