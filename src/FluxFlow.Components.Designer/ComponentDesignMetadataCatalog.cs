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
            var metadataItems = provider.GetMetadata()
                ?? throw new InvalidOperationException(
                    $"Design metadata provider '{provider.GetType().FullName}' returned a null metadata collection.");

            foreach (var metadata in metadataItems)
                catalog.Add(metadata);
        }

        return catalog;
    }

    public ComponentDesignMetadataCatalog Add(ComponentDesignMetadata metadata)
    {
        ComponentDesignMetadataValidator.ThrowIfInvalid(metadata);
        var snapshot = Snapshot(metadata);

        if (!_metadata.TryAdd(snapshot.Type, snapshot))
        {
            throw new InvalidOperationException(
                $"Design metadata for component type '{snapshot.Type}' is already registered.");
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

    private static ComponentDesignMetadata Snapshot(ComponentDesignMetadata metadata)
        => metadata with
        {
            Options = metadata.Options.Select(Snapshot).ToArray(),
            Resources = metadata.Resources.Select(Snapshot).ToArray(),
            Ports = metadata.Ports.Select(Snapshot).ToArray(),
            Attributes = Snapshot(metadata.Attributes)
        };

    private static OptionDesignMetadata Snapshot(OptionDesignMetadata option)
        => option with
        {
            Choices = option.Choices.Select(Snapshot).ToArray(),
            Attributes = Snapshot(option.Attributes)
        };

    private static OptionChoiceMetadata Snapshot(OptionChoiceMetadata choice)
        => choice with
        {
            Attributes = Snapshot(choice.Attributes)
        };

    private static ResourceDesignMetadata Snapshot(ResourceDesignMetadata resource)
        => resource with
        {
            Attributes = Snapshot(resource.Attributes)
        };

    private static PortDesignMetadata Snapshot(PortDesignMetadata port)
        => port with
        {
            Attributes = Snapshot(port.Attributes)
        };

    private static IReadOnlyDictionary<string, string> Snapshot(
        IReadOnlyDictionary<string, string> attributes)
        => new Dictionary<string, string>(attributes, StringComparer.Ordinal);
}
