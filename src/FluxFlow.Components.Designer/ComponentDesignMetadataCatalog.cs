using System.Diagnostics.CodeAnalysis;
using FluxFlow.Components.Designer.Contracts;

namespace FluxFlow.Components.Designer;

public sealed class ComponentDesignMetadataCatalog
{
    private readonly Dictionary<ComponentType, ComponentDesignMetadata> _metadata = [];

    public IReadOnlyCollection<ComponentDesignMetadata> All => _metadata.Values.ToArray();

    public static ComponentDesignMetadataCatalog FromProviders(IEnumerable<IComponentDesignMetadataProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);

        var catalog = new ComponentDesignMetadataCatalog();
        foreach (var provider in providers)
        {
            ArgumentNullException.ThrowIfNull(provider);
            foreach (var metadata in provider.GetMetadata())
                catalog.Add(metadata);
        }

        return catalog;
    }

    public ComponentDesignMetadataCatalog Add(ComponentDesignMetadata metadata)
    {
        ComponentDesignMetadataValidator.ThrowIfInvalid(metadata);

        if (!_metadata.TryAdd(metadata.Type, metadata))
        {
            throw new InvalidOperationException(
                $"Design metadata for component type '{metadata.Type}' is already registered.");
        }

        return this;
    }

    public ComponentDesignMetadataCatalog AddRange(IEnumerable<ComponentDesignMetadata> metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        foreach (var item in metadata)
            Add(item);

        return this;
    }

    public bool TryGet(ComponentType type, [NotNullWhen(true)] out ComponentDesignMetadata? metadata)
        => _metadata.TryGetValue(type, out metadata);
}
