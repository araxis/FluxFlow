using FluxFlow.Components.Designer.Contracts;

namespace FluxFlow.Components.Designer;

public static class ResourcePresentationExtensions
{
    public static ComponentDesignMetadata WithResource(this ComponentDesignMetadata metadata,
        string name, string? displayName, int order = 0, string? summary = null,
        string? designValueType = null, string? ownership = null, string? pickerKind = null,
        string? keyPattern = null, string? option = null, string? requiredWhenAnyOption = null)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var key = new ComponentResourceName(name.Trim());
        return metadata with
        {
            Resources = MetadataEdit.Replace(metadata.Resources, resource => resource.Name == key, resource => resource with
            {
                DisplayName = MetadataEdit.Text(displayName), Order = order, Summary = MetadataEdit.Text(summary),
                ValueType = designValueType is null ? resource.ValueType : new(designValueType),
                Attributes = MetadataEdit.Merge(resource.Attributes,
                    (ResourceDesignMetadataAttributeNames.Ownership, ownership),
                    (ResourceDesignMetadataAttributeNames.PickerKind, pickerKind),
                    (ResourceDesignMetadataAttributeNames.KeyPattern, keyPattern),
                    (ResourceDesignMetadataAttributeNames.Option, option),
                    (ResourceDesignMetadataAttributeNames.RequiredWhenAnyOption, requiredWhenAnyOption))
            }, $"Component resource '{key}' is not registered.")
        };
    }
}
