using FluxFlow.Components.Designer.Contracts;

namespace FluxFlow.Components.Designer;

/// <summary>Immutable presentation edits; component structure remains descriptor-owned.</summary>
public static class MetadataPresentationExtensions
{
    public static ComponentDesignMetadata WithDisplay(this ComponentDesignMetadata metadata,
        string? displayName = null, string? category = null, string? summary = null,
        string? iconKey = null, string? preferredNodeName = null, int? suggestedEditorWidth = null)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        return metadata with
        {
            DisplayName = MetadataEdit.Text(displayName),
            Category = category is null ? null : new(category),
            Summary = MetadataEdit.Text(summary),
            IconKey = iconKey is null ? null : new(iconKey),
            PreferredNodeName = preferredNodeName is null ? null : new(preferredNodeName),
            SuggestedEditorWidth = suggestedEditorWidth
        };
    }

    public static ComponentDesignMetadata WithAttribute(this ComponentDesignMetadata metadata,
        string name, string value, string? appendExistingWith = null)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        var key = new ComponentAttributeName(name);
        var attributes = new Dictionary<ComponentAttributeName, ComponentAttributeValue>(metadata.Attributes);
        if (appendExistingWith is not null && attributes.TryGetValue(key, out var existing))
            value = value + appendExistingWith + existing.Value;
        attributes[key] = new(value);
        return metadata with { Attributes = attributes };
    }
}

