using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.Validation.Composition;
using FluxFlow.Components.Validation.Options;
using FluxFlow.Composition;

namespace FluxFlow.Components.Validation.Designer;

public static class ValidationDesignExtensions
{
    public static FluxFlowRegistrationBuilder AddValidationDesign(this FluxFlowRegistrationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddDesignMetadata(ValidationComponentDefinition.Types.JsonSchemaValidator, Describe);
    }

    private static ComponentDesignMetadata Describe(ComponentDesignMetadata metadata)
    {
        var options = new Dictionary<string, Func<OptionDesignMetadata, OptionDesignMetadata>>(StringComparer.Ordinal)
        {
            [ValidationComponentDefinition.Options.Schema] = option => option with
            {
                DisplayName = new("Schema"),
                HelperText = new("Inline JSON schema compiled during composition build."),
                Kind = OptionValueKind.Json,
                Attributes = OptionAttributes("Schema", OptionDesignMetadataAttributeValues.Primary, OptionDesignMetadataAttributeValues.Json)
            },
            [ValidationComponentDefinition.Options.SchemaPath] = option => option with
            {
                DisplayName = new("Schema Path"),
                HelperText = new("Path to a JSON schema file read during composition build."),
                Kind = OptionValueKind.Text,
                Attributes = OptionAttributes("Schema", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Text)
            },
            [ValidationComponentDefinition.Options.SchemaId] = option => option with
            {
                DisplayName = new("Schema ID"),
                HelperText = new("Optional schema identifier used in results and diagnostics."),
                Kind = OptionValueKind.Text,
                Attributes = OptionAttributes("Diagnostics", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Text)
            },
            [ValidationComponentDefinition.Options.InputType] = option => option with
            {
                DisplayName = new("Input Type"),
                HelperText = new("Diagnostic type metadata; the canonical input is JsonElement."),
                Kind = OptionValueKind.Text,
                DefaultValue = JsonSchemaValidatorOptions.ObjectTypeName,
                Attributes = OptionAttributes("Type Metadata", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Text)
            },
            [ValidationComponentDefinition.Options.ValueSelector] = option => option with
            {
                DisplayName = new("Value Selector"),
                HelperText = new("Selector name passed to the optional host-owned selector resource."),
                Kind = OptionValueKind.Text,
                DefaultValue = JsonSchemaValidatorOptions.DefaultValueSelector,
                Attributes = OptionAttributes("Selection", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Text, null, ValidationComponentDefinition.Resources.Selector)
            },
            [ValidationComponentDefinition.Options.BoundedCapacity] = option => option with
            {
                DisplayName = new("Bounded Capacity"),
                HelperText = new("Capacity used for bounded processing and reliable normal-data output."),
                Kind = OptionValueKind.Number,
                DefaultValue = 128,
                Min = 1,
                Attributes = OptionAttributes("Runtime", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Number)
            }
        };
        var ports = new Dictionary<string, (string Group, int Order, string Summary, bool Primary)>(StringComparer.Ordinal)
        {
            [ValidationComponentDefinition.Ports.Input] = ("Messages", 0, "Immutable workflow value to validate.", true),
            [ValidationComponentDefinition.Ports.Output] = ("Results", 1, "Normal valid, invalid, or processing-failure result.", true),
            [ValidationComponentDefinition.Ports.Events] = ("Diagnostics", 2, "Best-effort validation diagnostics.", false)
        };
        var resources = new Dictionary<string, (string Label, int Order, string Summary, string Picker, string Key)>(StringComparer.Ordinal)
        {
            [ValidationComponentDefinition.Resources.Selector] = ("Selector", 0, "Optional keyed JSON schema value selector used to choose the value to validate.", ResourceDesignMetadataAttributeValues.Selector, "Resources.{name}"),
            [ValidationComponentDefinition.Resources.Clock] = ("Clock", 1, "Optional keyed clock for deterministic validation results and diagnostics.", ResourceDesignMetadataAttributeValues.Clock, "Resources.{name}")
        };
        return metadata with
        {
            DisplayName = new("JSON Schema Validator"),
            Category = new("Validation"),
            Summary = new("Validates schema-less JSON values against an inline or path-based JSON schema."),
            IconKey = new("shield-check"),
            PreferredNodeName = new("validate"),
            SuggestedEditorWidth = 460,
            Options = metadata.Options.Select(option => options.TryGetValue(option.Name.Value, out var describe)
                ? describe(option) : option).ToArray(),
            Ports = metadata.Ports.Select(port => ports.TryGetValue(port.Name.Value, out var hint)
                ? port with
                {
                    Group = new(hint.Group), Order = hint.Order,
                    Summary = new(hint.Summary), IsPrimary = hint.Primary
                } : port).ToArray(),
            Resources = metadata.Resources.Select(resource => resources.TryGetValue(resource.Name.Value, out var hint)
                ? resource with
                {
                    DisplayName = new(hint.Label), Order = hint.Order, Summary = new(hint.Summary),
                    Attributes = new Dictionary<ComponentAttributeName, ComponentAttributeValue>
                    {
                        [new(ResourceDesignMetadataAttributeNames.Ownership)] = new(ResourceDesignMetadataAttributeValues.HostOwned),
                        [new(ResourceDesignMetadataAttributeNames.PickerKind)] = new(hint.Picker),
                        [new(ResourceDesignMetadataAttributeNames.KeyPattern)] = new(hint.Key)
                    }
                } : resource).ToArray()
        };
    }

    private static IReadOnlyDictionary<ComponentAttributeName, ComponentAttributeValue> OptionAttributes(
        string section, string importance, string? editor, string? syntax = null, string? relatedResource = null)
    {
        var attributes = new Dictionary<ComponentAttributeName, ComponentAttributeValue>
        {
            [new(OptionDesignMetadataAttributeNames.Section)] = new(section),
            [new(OptionDesignMetadataAttributeNames.Importance)] = new(importance)
        };
        if (editor is not null) attributes.Add(new(OptionDesignMetadataAttributeNames.Editor), new(editor));
        if (syntax is not null) attributes.Add(new(OptionDesignMetadataAttributeNames.Syntax), new(syntax));
        if (relatedResource is not null) attributes.Add(new(OptionDesignMetadataAttributeNames.RelatedResource), new(relatedResource));
        return attributes;
    }
}
