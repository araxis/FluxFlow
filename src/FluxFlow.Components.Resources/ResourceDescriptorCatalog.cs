using FluxFlow.Components.Resources.Contracts;

namespace FluxFlow.Components.Resources;

public sealed class ResourceDescriptorCatalog : IResourceLookup
{
    private readonly Dictionary<ResourceName, ResourceDescriptor> _resources;

    public ResourceDescriptorCatalog(IEnumerable<ResourceDescriptor> resources)
    {
        ArgumentNullException.ThrowIfNull(resources);

        var materialized = resources.ToArray();
        ResourceDiagnostics.ThrowIfInvalid(materialized);
        _resources = materialized.ToDictionary(resource => resource.Name);
    }

    public IReadOnlyCollection<ResourceDescriptor> GetResources() => _resources.Values.ToArray();

    public ValueTask<ResourceLookupResult> LookupAsync(
        ResourceReference reference,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ResourceDiagnostics.ThrowIfInvalid(reference);

        if (!_resources.TryGetValue(reference.Name, out var descriptor))
            return ValueTask.FromResult(ResourceLookupResult.Missing(reference));

        if (!string.IsNullOrWhiteSpace(reference.Kind)
            && !string.Equals(reference.Kind, descriptor.Kind, StringComparison.Ordinal))
        {
            return ValueTask.FromResult(ResourceLookupResult.KindMismatch(reference, descriptor));
        }

        return ValueTask.FromResult(ResourceLookupResult.FoundResult(reference, descriptor));
    }
}
