namespace FluxFlow.Components.Designer.Contracts;

public static class OptionDesignMetadataAttributeNames
{
    public const string Section = "section";
    public const string Importance = "importance";
    public const string Editor = "editor";
    public const string Syntax = "syntax";
    public const string RelatedResource = "relatedResource";
}

public static class OptionDesignMetadataAttributeValues
{
    public const string Primary = "primary";
    public const string Advanced = "advanced";
    public const string Text = "text";
    public const string Number = "number";
    public const string Expression = "expression";
    public const string Json = "json";
}

public static class OptionDesignMetadataAttributes
{
    public static IReadOnlyDictionary<string, string> Create(
        string? section = null,
        string? importance = null,
        string? editor = null,
        string? syntax = null,
        string? relatedResource = null)
    {
        var attributes = new Dictionary<string, string>();

        AddIfPresent(attributes, OptionDesignMetadataAttributeNames.Section, section);
        AddIfPresent(attributes, OptionDesignMetadataAttributeNames.Importance, importance);
        AddIfPresent(attributes, OptionDesignMetadataAttributeNames.Editor, editor);
        AddIfPresent(attributes, OptionDesignMetadataAttributeNames.Syntax, syntax);
        AddIfPresent(attributes, OptionDesignMetadataAttributeNames.RelatedResource, relatedResource);

        return attributes;
    }

    public static IReadOnlyDictionary<ComponentAttributeName, ComponentAttributeValue> CreateMap(
        string? section = null,
        string? importance = null,
        string? editor = null,
        string? syntax = null,
        string? relatedResource = null)
        => Create(section, importance, editor, syntax, relatedResource)
            .ToDictionary(
                attribute => new ComponentAttributeName(attribute.Key),
                attribute => new ComponentAttributeValue(attribute.Value));

    private static void AddIfPresent(
        IDictionary<string, string> attributes,
        string name,
        string? value)
    {
        if (value is null)
            return;
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"Option metadata attribute '{name}' cannot be empty.", nameof(value));

        attributes.Add(name, value);
    }
}
