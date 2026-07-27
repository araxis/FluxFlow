using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Composition;

namespace FluxFlow.Components.Designer;

public sealed class ComponentDesignDeclaration
{
    public ComponentDesignDeclaration(
        ComponentDescriptor descriptor,
        ComponentDesignMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(metadata);

        if (!string.Equals(descriptor.Type, metadata.Type.Value, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Design metadata type '{metadata.Type}' does not match component descriptor type '{descriptor.Type}'.",
                nameof(metadata));
        }

        Descriptor = descriptor;
        Metadata = metadata;
    }

    public ComponentDescriptor Descriptor { get; }

    public ComponentDesignMetadata Metadata { get; }

    public static IReadOnlyCollection<ComponentDesignDeclaration> CreateRange(
        IEnumerable<ComponentDescriptor> descriptors,
        IEnumerable<ComponentDesignMetadata> metadata)
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        ArgumentNullException.ThrowIfNull(metadata);

        var descriptorsByType = descriptors.ToDictionary(
            static descriptor => descriptor.Type,
            StringComparer.Ordinal);
        var metadataByType = metadata.ToDictionary(
            static item => item.Type.Value,
            StringComparer.Ordinal);

        var missingMetadata = descriptorsByType.Keys
            .Except(metadataByType.Keys, StringComparer.Ordinal)
            .OrderBy(static type => type, StringComparer.Ordinal)
            .ToArray();
        var missingDescriptors = metadataByType.Keys
            .Except(descriptorsByType.Keys, StringComparer.Ordinal)
            .OrderBy(static type => type, StringComparer.Ordinal)
            .ToArray();
        if (missingMetadata.Length > 0 || missingDescriptors.Length > 0)
        {
            throw new ArgumentException(
                $"Component declarations must pair exactly. " +
                $"Missing metadata: [{string.Join(", ", missingMetadata)}]. " +
                $"Missing descriptors: [{string.Join(", ", missingDescriptors)}].");
        }

        return descriptorsByType.Values
            .OrderBy(static descriptor => descriptor.Type, StringComparer.Ordinal)
            .Select(descriptor => new ComponentDesignDeclaration(
                descriptor,
                metadataByType[descriptor.Type]))
            .ToArray();
    }
}
