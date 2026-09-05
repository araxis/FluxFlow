using FluxFlow.Components.Designer.Contracts;

namespace FluxFlow.Components.Designer;

public static class ComponentResourcePickerHints
{
    private static readonly ComponentAttributeName OwnershipAttribute =
        new(ResourceDesignMetadataAttributeNames.Ownership);

    private static readonly ComponentAttributeName PickerKindAttribute =
        new(ResourceDesignMetadataAttributeNames.PickerKind);

    private static readonly ComponentAttributeName KeyPatternAttribute =
        new(ResourceDesignMetadataAttributeNames.KeyPattern);

    private static readonly ComponentAttributeName OptionAttribute =
        new(ResourceDesignMetadataAttributeNames.Option);

    private static readonly ComponentAttributeName RequiredWhenAnyOptionAttribute =
        new(ResourceDesignMetadataAttributeNames.RequiredWhenAnyOption);

    public static IReadOnlyList<ComponentResourcePickerHint> Create(ComponentDesignMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        return metadata.Resources
            .OrderBy(resource => resource.Order)
            .ThenBy(resource => resource.Name.Value, StringComparer.Ordinal)
            .Select(resource => Create(metadata.Type, resource))
            .Where(hint => hint is not null)
            .Select(hint => hint!)
            .ToArray();
    }

    public static IReadOnlyList<ComponentResourcePickerHint> Create(ComponentDesignMetadataCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        return catalog.All
            .OrderBy(metadata => metadata.Type.Value, StringComparer.Ordinal)
            .SelectMany(Create)
            .ToArray();
    }

    private static ComponentResourcePickerHint? Create(
        ComponentType componentType,
        ResourceDesignMetadata resource)
    {
        if (!HasHostOwnedOwnership(resource) ||
            !TryGetAttribute(resource, PickerKindAttribute, out var pickerKind) ||
            string.IsNullOrWhiteSpace(pickerKind))
        {
            return null;
        }

        return new ComponentResourcePickerHint
        {
            ComponentType = componentType,
            ResourceName = resource.Name,
            PickerKind = pickerKind,
            KeyPattern = GetOptionalAttribute(resource, KeyPatternAttribute),
            RelatedOption = CreateOptionalOptionName(GetOptionalAttribute(resource, OptionAttribute)),
            RequiredWhenAnyOptions = CreateConditionalOptionNames(
                GetOptionalAttribute(resource, RequiredWhenAnyOptionAttribute)),
            IsRequired = resource.IsRequired,
            ValueType = resource.ValueType,
            DisplayName = resource.DisplayName,
            Summary = resource.Summary
        };
    }

    private static bool HasHostOwnedOwnership(ResourceDesignMetadata resource)
        => TryGetAttribute(resource, OwnershipAttribute, out var ownership) &&
           string.Equals(
               ownership,
               ResourceDesignMetadataAttributeValues.HostOwned,
               StringComparison.Ordinal);

    private static string? GetOptionalAttribute(
        ResourceDesignMetadata resource,
        ComponentAttributeName attributeName)
        => TryGetAttribute(resource, attributeName, out var value) &&
           !string.IsNullOrWhiteSpace(value)
            ? value
            : null;

    private static bool TryGetAttribute(
        ResourceDesignMetadata resource,
        ComponentAttributeName attributeName,
        out string value)
    {
        if (resource.Attributes.TryGetValue(attributeName, out var attributeValue))
        {
            value = attributeValue.Value;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static ComponentOptionName? CreateOptionalOptionName(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? null
            : new ComponentOptionName(value);

    private static IReadOnlyList<ComponentOptionName> CreateConditionalOptionNames(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? []
            : value
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(option => !string.IsNullOrWhiteSpace(option))
                .Select(option => new ComponentOptionName(option))
                .ToArray();
}
