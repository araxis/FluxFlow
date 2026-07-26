namespace FluxFlow.Components.Designer.Contracts;

public static class OptionDesignMetadataFactory
{
    public static OptionDesignMetadata Text(
        string name,
        string displayName,
        string helperText,
        string section,
        string importance,
        string? defaultValue = null,
        bool isRequired = false,
        string? relatedResource = null)
        => Create(
            name,
            OptionValueKind.Text,
            displayName,
            helperText,
            section,
            importance,
            OptionDesignMetadataAttributeValues.Text,
            defaultValue,
            isRequired,
            relatedResource: relatedResource);

    public static OptionDesignMetadata Number(
        string name,
        string displayName,
        string helperText,
        string section,
        string importance,
        object? defaultValue = null,
        double? min = null,
        double? max = null,
        bool isRequired = false)
        => Create(
            name,
            OptionValueKind.Number,
            displayName,
            helperText,
            section,
            importance,
            OptionDesignMetadataAttributeValues.Number,
            defaultValue,
            isRequired,
            min,
            max);

    public static OptionDesignMetadata Boolean(
        string name,
        string displayName,
        string helperText,
        string section,
        string importance,
        bool? defaultValue = null,
        bool isRequired = false)
        => Create(
            name,
            OptionValueKind.Boolean,
            displayName,
            helperText,
            section,
            importance,
            editor: null,
            defaultValue,
            isRequired);

    public static OptionDesignMetadata Json(
        string name,
        string displayName,
        string helperText,
        string section,
        string importance,
        object? defaultValue = null,
        bool isRequired = false,
        string? relatedResource = null)
        => Create(
            name,
            OptionValueKind.Json,
            displayName,
            helperText,
            section,
            importance,
            OptionDesignMetadataAttributeValues.Json,
            defaultValue,
            isRequired,
            relatedResource: relatedResource);

    public static OptionDesignMetadata Expression(
        string name,
        string displayName,
        string helperText,
        string section,
        string importance,
        string? defaultValue = null,
        bool isRequired = false,
        string? relatedResource = null)
        => Create(
            name,
            OptionValueKind.Expression,
            displayName,
            helperText,
            section,
            importance,
            OptionDesignMetadataAttributeValues.Expression,
            defaultValue,
            isRequired,
            syntax: OptionDesignMetadataAttributeValues.Expression,
            relatedResource: relatedResource);

    public static OptionDesignMetadata BoundedCapacity(
        int defaultValue,
        string helperText = "Maximum queued input messages.")
        => Number(
            "boundedCapacity",
            "Bounded Capacity",
            helperText,
            "Runtime",
            OptionDesignMetadataAttributeValues.Advanced,
            defaultValue,
            min: 1);

    public static OptionDesignMetadata TypeName(
        string name,
        string displayName,
        string defaultValue,
        string helperText)
        => Text(
            name,
            displayName,
            helperText,
            "Type Metadata",
            OptionDesignMetadataAttributeValues.Advanced,
            defaultValue);

    private static OptionDesignMetadata Create(
        string name,
        OptionValueKind kind,
        string displayName,
        string helperText,
        string section,
        string importance,
        string? editor,
        object? defaultValue,
        bool isRequired,
        double? min = null,
        double? max = null,
        string? syntax = null,
        string? relatedResource = null)
        => new()
        {
            Name = new ComponentOptionName(name),
            Kind = kind,
            DisplayName = new ComponentMetadataText(displayName),
            HelperText = new ComponentMetadataText(helperText),
            IsRequired = isRequired,
            DefaultValue = defaultValue,
            Min = min,
            Max = max,
            Attributes = OptionDesignMetadataAttributes.CreateMap(
                section,
                importance,
                editor,
                syntax,
                relatedResource)
        };
}
