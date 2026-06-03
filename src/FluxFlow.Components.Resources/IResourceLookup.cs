using FluxFlow.Components.Resources.Contracts;

namespace FluxFlow.Components.Resources;

public interface IResourceLookup
{
    IReadOnlyCollection<ResourceDescriptor> GetResources();

    ValueTask<ResourceLookupResult> LookupAsync(
        ResourceReference reference,
        CancellationToken cancellationToken = default);
}
