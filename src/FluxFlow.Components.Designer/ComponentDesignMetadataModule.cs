using FluxFlow.Components.Designer.Contracts;

namespace FluxFlow.Components.Designer;

public sealed class ComponentDesignMetadataModule : IComponentDesignMetadataProvider
{
    private readonly IReadOnlyCollection<ComponentDesignMetadata> _metadata;

    public ComponentDesignMetadataModule(IEnumerable<ComponentDesignMetadata> metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        _metadata = metadata.ToArray();
    }

    public IReadOnlyCollection<ComponentDesignMetadata> GetMetadata() => _metadata;
}
