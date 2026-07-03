using FluxFlow.Components.Resources.Contracts;

namespace FluxFlow.Components.Resources;

public sealed class ResourceDescriptorCatalogBuilder
{
    private readonly List<ResourceDescriptor> descriptors = [];

    public ResourceDescriptorCatalogBuilder Add(ResourceDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        descriptors.Add(descriptor);
        return this;
    }

    public ResourceDescriptorCatalogBuilder AddRange(IEnumerable<ResourceDescriptor> resources)
    {
        ArgumentNullException.ThrowIfNull(resources);

        foreach (var descriptor in resources)
            Add(descriptor);

        return this;
    }

    public ResourceDescriptorCatalogBuilder Add(
        string name,
        string? kind = null,
        string? displayName = null,
        string? summary = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        ArgumentNullException.ThrowIfNull(name);

        var descriptor = new ResourceDescriptor
        {
            Name = new ResourceName(name),
            Kind = kind,
            DisplayName = displayName,
            Summary = summary
        };

        if (metadata is not null)
        {
            descriptor = descriptor with
            {
                Metadata = metadata
            };
        }

        return Add(descriptor);
    }

    public ResourceDescriptorCatalogBuilder Add(
        ResourceName name,
        ResourceKind? kind = null,
        ResourceMetadataText? displayName = null,
        ResourceMetadataText? summary = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        if (string.IsNullOrWhiteSpace(name.Value))
            throw new ArgumentException("Resource name cannot be empty.", nameof(name));

        var descriptor = new ResourceDescriptor
        {
            Name = name,
            Kind = kind?.Value,
            DisplayName = displayName?.Value,
            Summary = summary?.Value
        };

        if (metadata is not null)
        {
            descriptor = descriptor with
            {
                Metadata = metadata
            };
        }

        return Add(descriptor);
    }

    public IReadOnlyList<ResourceDescriptor> BuildDescriptors()
        => descriptors.ToArray();

    public ResourceDescriptorCatalog BuildCatalog()
        => new(BuildDescriptors());
}
