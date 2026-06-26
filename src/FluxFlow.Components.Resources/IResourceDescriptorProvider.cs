using FluxFlow.Components.Resources.Contracts;

namespace FluxFlow.Components.Resources;

public interface IResourceDescriptorProvider
{
    IReadOnlyCollection<ResourceDescriptor> GetResources();
}
