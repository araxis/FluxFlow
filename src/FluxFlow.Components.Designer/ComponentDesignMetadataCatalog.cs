using System.Collections.Frozen;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using FluxFlow.Components.Designer.Contracts;

namespace FluxFlow.Components.Designer;

public sealed class ComponentDesignMetadataCatalog
{
    private readonly FrozenDictionary<ComponentType, ComponentDesignMetadata> metadataByType;

    public ComponentDesignMetadataCatalog(IEnumerable<ComponentDesignMetadata>? metadata = null)
        : this(metadata ?? [], finalize: true)
    {
    }

    private ComponentDesignMetadataCatalog(
        IEnumerable<ComponentDesignMetadata> metadata,
        bool finalize)
    {
        var items = new List<ComponentDesignMetadata>();
        var index = new Dictionary<ComponentType, ComponentDesignMetadata>();

        foreach (var item in metadata)
        {
            if (item is null)
            {
                throw new ArgumentException(
                    "Component design metadata cannot contain null values.",
                    nameof(metadata));
            }

            var snapshot = finalize
                ? ComponentDesignMetadataFinalizer.Finalize(item)
                : item;
            if (!index.TryAdd(snapshot.Type, snapshot))
            {
                throw new InvalidOperationException(
                    $"Design metadata for component type '{snapshot.Type}' is already registered.");
            }

            items.Add(snapshot);
        }

        metadataByType = index.ToFrozenDictionary();
        All = new ReadOnlyCollection<ComponentDesignMetadata>(items);
    }

    public IReadOnlyList<ComponentDesignMetadata> All { get; }

    internal static ComponentDesignMetadataCatalog FromDeclarations(
        IEnumerable<ComponentDesignDeclaration> declarations)
    {
        ArgumentNullException.ThrowIfNull(declarations);
        return new ComponentDesignMetadataCatalog(
            declarations.Select(static declaration =>
                (declaration ?? throw new ArgumentException(
                    "Component design declarations cannot contain null values.",
                    nameof(declarations))).Metadata),
            finalize: false);
    }

    public bool TryGet(
        ComponentType type,
        [NotNullWhen(true)] out ComponentDesignMetadata? metadata)
        => metadataByType.TryGetValue(type, out metadata);
}
