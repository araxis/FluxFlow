using System.Text.Json;
using System.Text.Json.Nodes;

namespace FluxFlow.Environments;

/// <summary>Available providers and profile snapshots for one project connection type.</summary>
public sealed class ServiceBindingSelection(string resourceType, EnvironmentDefaults defaults, IEnumerable<EnvironmentProviderDescription> providers)
{
    public IReadOnlyList<EnvironmentProviderDescription> Providers { get; } = providers.Where(p => p.ResourceType == resourceType).ToArray();
    public IEnumerable<ContainerProfile> Profiles => defaults.Profiles.Where(p => Providers.Any(provider => provider.Name == p.Provider));

    public ResourceBinding SelectProvider(ResourceBinding current, string name)
    {
        if (name == current.Provider) return current;
        if (name == "existing") return new();
        var provider = Providers.FirstOrDefault(p => p.Name == name);
        if (provider is null) return current;
        if (defaults.Profiles.FirstOrDefault(p => p.Provider == name && p.DefaultForResourceType == resourceType) is { } profile)
            return profile.CreateBinding(defaults.Registries);

        var registry = defaults.Registries.FirstOrDefault()
            ?? throw new InvalidOperationException("Add an image registry before setting up a container.");
        return new() { Provider = name, Image = $"{registry.Address}/{ContainerImageReference.WithoutRegistry(provider.Image)}" };
    }

    public ResourceBinding SelectProfile(ResourceBinding current, string id)
    {
        var profile = Profiles.FirstOrDefault(p => p.Id == id);
        if (profile is null) return current;
        var snapshot = profile.CreateBinding(defaults.Registries);
        // A profile can be reapplied after either its definition or its editable snapshot changes.
        var type = EnvironmentJsonContext.Default.ResourceBinding;
        return current.Profile?.Id == id && JsonNode.DeepEquals(JsonSerializer.SerializeToNode(current, type), JsonSerializer.SerializeToNode(snapshot, type))
            ? current : snapshot;
    }
}
