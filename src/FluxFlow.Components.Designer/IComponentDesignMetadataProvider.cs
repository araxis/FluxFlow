using FluxFlow.Components.Designer.Contracts;

namespace FluxFlow.Components.Designer;

public interface IComponentDesignMetadataProvider
{
    IReadOnlyCollection<ComponentDesignMetadata> GetMetadata();
}
