using System.Diagnostics.CodeAnalysis;
using FluxFlow.Components.Designer.Contracts;

namespace FluxFlow.Components.Designer;

public sealed class ComponentDesignMetadataCatalog
{
    private readonly Dictionary<ComponentType, ComponentDesignMetadata> _metadata = [];
    private readonly Dictionary<ComponentType, ComponentType> _aliases = [];

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
        var aliases = ReadAliases(snapshot);

        if (_metadata.ContainsKey(snapshot.Type) || _aliases.ContainsKey(snapshot.Type))
        {
            throw new InvalidOperationException(
                $"Design metadata or alias for component type '{snapshot.Type}' is already registered.");
        }

        foreach (var alias in aliases)
        {
            if (_metadata.ContainsKey(alias) || _aliases.ContainsKey(alias))
            {
                throw new InvalidOperationException(
                    $"Design metadata or alias for component type '{alias}' is already registered.");
            }
        }

        _metadata.Add(snapshot.Type, snapshot);
        foreach (var alias in aliases)
            _aliases.Add(alias, snapshot.Type);

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
    {
        if (_metadata.TryGetValue(type, out metadata))
            return true;

        return _aliases.TryGetValue(type, out var canonicalType) &&
               _metadata.TryGetValue(canonicalType, out metadata);
    }

    private static IReadOnlyList<ComponentType> ReadAliases(ComponentDesignMetadata metadata)
    {
        var attributeName = new ComponentAttributeName(ComponentDesignMetadataAttributeNames.Aliases);
        if (!metadata.Attributes.TryGetValue(attributeName, out var aliases))
            return [];

        var parsed = aliases.Value
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(static alias => new ComponentType(alias))
            .Distinct()
            .ToArray();
        if (parsed.Length == 0)
        {
            throw new InvalidOperationException(
                $"Design metadata aliases for component type '{metadata.Type}' cannot be empty.");
        }
        if (parsed.Contains(metadata.Type))
        {
            throw new InvalidOperationException(
                $"Design metadata alias for component type '{metadata.Type}' must differ from its canonical type.");
        }

        return parsed;
    }

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

    private static IReadOnlyDictionary<ComponentAttributeName, ComponentAttributeValue> Snapshot(
        IReadOnlyDictionary<ComponentAttributeName, ComponentAttributeValue> attributes)
        => new Dictionary<ComponentAttributeName, ComponentAttributeValue>(attributes);
}
