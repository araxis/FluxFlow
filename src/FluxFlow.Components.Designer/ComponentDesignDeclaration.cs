using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Composition;

namespace FluxFlow.Components.Designer;

internal sealed class ComponentDesignDeclaration
{
    internal ComponentDesignDeclaration(
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

}
