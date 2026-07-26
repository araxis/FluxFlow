namespace FluxFlow.Components.Designer.Contracts;

public static class ResourceDesignMetadataFactory
{
    public static ResourceDesignMetadata HostOwned(
        string name,
        string pickerKind,
        string displayName,
        int order,
        string summary,
        string valueType,
        bool isRequired = false,
        string? keyPattern = null,
        string? option = null,
        string? requiredWhenAnyOption = null)
        => new()
        {
            Name = new ComponentResourceName(name),
            DisplayName = new ComponentMetadataText(displayName),
            Order = order,
            Summary = new ComponentMetadataText(summary),
            ValueType = new ComponentValueTypeHint(valueType),
            IsRequired = isRequired,
            Attributes = ResourceDesignMetadataAttributes.CreateHostOwnedMap(
                pickerKind,
                keyPattern,
                option,
                requiredWhenAnyOption)
        };

    public static ResourceDesignMetadata Clock(
        string name,
        int order,
        string summary)
        => HostOwned(
            name,
            ResourceDesignMetadataAttributeValues.Clock,
            "Clock",
            order,
            summary,
            nameof(TimeProvider),
            keyPattern: "clock:{name}");
}
