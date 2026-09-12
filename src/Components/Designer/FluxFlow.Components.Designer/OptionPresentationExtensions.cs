using FluxFlow.Components.Designer.Contracts;

namespace FluxFlow.Components.Designer;

public static class OptionPresentationExtensions
{
    public static ComponentDesignMetadata WithOption(this ComponentDesignMetadata metadata,
        string name, OptionValueKind kind, string? displayName = null, string? helperText = null,
        object? defaultValue = null, double? min = null, double? max = null,
        string? section = null, string? importance = null, string? editor = null,
        string? syntax = null, string? relatedResource = null)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var key = new ComponentOptionName(name.Trim());
        return metadata with
        {
            Options = MetadataEdit.Replace(metadata.Options, option => option.Name == key, option => option with
            {
                Kind = kind, DisplayName = MetadataEdit.Text(displayName),
                HelperText = MetadataEdit.Text(helperText), DefaultValue = defaultValue, Min = min, Max = max,
                Attributes = MetadataEdit.Merge(option.Attributes,
                    (OptionDesignMetadataAttributeNames.Section, section),
                    (OptionDesignMetadataAttributeNames.Importance, importance),
                    (OptionDesignMetadataAttributeNames.Editor, editor),
                    (OptionDesignMetadataAttributeNames.Syntax, syntax),
                    (OptionDesignMetadataAttributeNames.RelatedResource, relatedResource))
            }, $"Component option '{key}' is not registered.")
        };
    }

    public static ComponentDesignMetadata WithOptionChoice(this ComponentDesignMetadata metadata,
        string optionName, string value, string? displayName = null, string? helperText = null)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentException.ThrowIfNullOrWhiteSpace(optionName);
        var key = new ComponentOptionName(optionName.Trim());
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var choiceValue = new ComponentOptionChoiceValue(value.Trim());
        return metadata with
        {
            Options = MetadataEdit.Replace(metadata.Options, option => option.Name == key, option =>
            {
                if (option.Choices.Any(choice => choice.Value == choiceValue))
                    throw new InvalidOperationException($"Option '{key}' choice '{choiceValue}' is already registered.");
                return option with
                {
                    Choices = [.. option.Choices, new OptionChoiceMetadata
                    {
                        Value = choiceValue, DisplayName = MetadataEdit.Text(displayName),
                        HelperText = MetadataEdit.Text(helperText)
                    }]
                };
            }, $"Component option '{key}' is not registered.")
        };
    }
}
