namespace FluxFlow.Components.Designer.Contracts;

public static class ResourceDesignMetadataAttributeNames
{
    public const string Ownership = "ownership";
    public const string PickerKind = "pickerKind";
    public const string KeyPattern = "keyPattern";
    public const string Option = "option";
    public const string RequiredWhenAnyOption = "requiredWhenAnyOption";
}

public static class ResourceDesignMetadataAttributeValues
{
    public const string HostOwned = "host-owned";
    public const string Clock = "clock";
    public const string Client = "client";
    public const string ContextFactory = "context-factory";
    public const string Delegate = "delegate";
    public const string ExpressionEngine = "expression-engine";
    public const string Publisher = "publisher";
    public const string Selector = "selector";
    public const string Store = "store";
    public const string TriggerSource = "trigger-source";
}

public static class ResourceDesignMetadataAttributes
{
    public static IReadOnlyDictionary<string, string> CreateHostOwned(
        string pickerKind,
        string? keyPattern = null,
        string? option = null,
        string? requiredWhenAnyOption = null)
    {
        if (string.IsNullOrWhiteSpace(pickerKind))
            throw new ArgumentException("Resource picker kind cannot be empty.", nameof(pickerKind));

        var attributes = new Dictionary<string, string>
        {
            [ResourceDesignMetadataAttributeNames.Ownership] = ResourceDesignMetadataAttributeValues.HostOwned,
            [ResourceDesignMetadataAttributeNames.PickerKind] = pickerKind
        };

        AddIfPresent(attributes, ResourceDesignMetadataAttributeNames.KeyPattern, keyPattern);
        AddIfPresent(attributes, ResourceDesignMetadataAttributeNames.Option, option);
        AddIfPresent(attributes, ResourceDesignMetadataAttributeNames.RequiredWhenAnyOption, requiredWhenAnyOption);

        return attributes;
    }

    public static IReadOnlyDictionary<ComponentAttributeName, ComponentAttributeValue> CreateHostOwnedMap(
        string pickerKind,
        string? keyPattern = null,
        string? option = null,
        string? requiredWhenAnyOption = null)
        => CreateHostOwned(pickerKind, keyPattern, option, requiredWhenAnyOption)
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
            throw new ArgumentException($"Resource metadata attribute '{name}' cannot be empty.", nameof(value));

        attributes.Add(name, value);
    }
}
