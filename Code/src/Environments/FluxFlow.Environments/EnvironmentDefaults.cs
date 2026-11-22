using System.Text.Json;
using System.Text.Json.Nodes;

namespace FluxFlow.Environments;

public sealed record ImageRegistry
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; init; } = "New registry";
    public string Address { get; init; } = "docker.io";
    public string Description { get; init; } = "";
    public string Authentication { get; init; } = "Docker credentials";

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || string.IsNullOrWhiteSpace(Name)) throw new ArgumentException("A registry name is required.");
        if (string.IsNullOrWhiteSpace(Address) || Address.StartsWith('-') || Address.Contains('/') || Address.Contains('@') || Address.Contains('?') || Address.Contains('#') ||
            !Uri.TryCreate($"https://{Address}", UriKind.Absolute, out var uri) || uri.Authority != Address || uri.HostNameType == UriHostNameType.Unknown)
            throw new ArgumentException("Use a registry host with an optional port, without a scheme, path or credentials.");
        if (Authentication is not ("Anonymous" or "Docker credentials")) throw new ArgumentException("Unknown registry authentication method.");
    }
}

public sealed record ContainerProfile
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; init; } = "New profile";
    public string Description { get; init; } = "";
    public string? RegistryId { get; init; }
    // Image-only profiles are independent of workflow resource types.
    public string? Provider { get; init; }
    public string? DefaultForResourceType { get; init; }
    public bool SetupLocked { get; init; }
    public ResourceBinding Settings { get; init; } = new();

    public string ResolveImage(IEnumerable<ImageRegistry> registries)
    {
        var image = Settings.Image;
        if (string.IsNullOrWhiteSpace(image) || image.Any(char.IsWhiteSpace) || image.Contains("://")) throw new ArgumentException("An image repository with a tag or digest is required.");
        if (string.IsNullOrWhiteSpace(RegistryId)) throw new ArgumentException("Select a configured registry for this profile.");
        var reference = ContainerImageReference.Parse(image);
        if (reference.Registry is not null || string.IsNullOrWhiteSpace(reference.Name) || reference.Name.StartsWith('/') ||
            reference.Name.Contains(':') || reference.Name.Contains('@') || string.IsNullOrWhiteSpace(reference.Version) ||
            reference.Version.Contains('/') || reference.Version.Contains('@') ||
            (!reference.IsDigest && reference.Version.Contains(':')))
            throw new ArgumentException("Enter an image name and tag or digest; select the registry separately.");
        var registry = registries.SingleOrDefault(item => item.Id == RegistryId) ?? throw new ArgumentException("The profile's registry no longer exists.");
        registry.Validate();
        return $"{registry.Address}/{image.TrimStart('/')}";
    }

    public ResourceBinding CreateBinding(IEnumerable<ImageRegistry> registries)
    {
        if (string.IsNullOrWhiteSpace(Provider)) throw new InvalidOperationException("An image-only profile cannot supply a workflow resource. Choose a service provider first.");
        var copy = JsonSerializer.Deserialize(JsonSerializer.Serialize(Settings, EnvironmentJsonContext.Default.ResourceBinding), EnvironmentJsonContext.Default.ResourceBinding)!;
        copy = copy with { Provider = Provider, Image = ResolveImage(registries), Profile = null };
        return copy with { Profile = new(Id, Name, SetupLocked, copy.CopySetup()) };
    }
}

public sealed record EnvironmentDefaults
{
    public List<InfrastructureHost> Infrastructure { get; set; } = [];
    public List<ImageRegistry> Registries { get; init; } = [];
    public List<ContainerProfile> Profiles { get; init; } = [];

    public string ValidateNewHostName(string name) => ValidateHostName(name, null);

    private string ValidateHostName(string name, InfrastructureHost? original)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0 || Infrastructure.Any(host => !ReferenceEquals(host, original) &&
            string.Equals(host.Name.Trim(), trimmed, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Choose a unique host name.");
        return trimmed;
    }

    public EnvironmentDefaults AddHost(string name) =>
        this with { Infrastructure = [.. Infrastructure, new InfrastructureHost { Name = ValidateNewHostName(name) }] };

    public EnvironmentDefaults UpdateHost(InfrastructureHost original, EnvironmentTopology topology,
        IEnumerable<IResourceEnvironmentAdapter>? adapters = null)
    {
        var index = Infrastructure.FindIndex(host => ReferenceEquals(host, original));
        if (index < 0) return this;

        ArgumentNullException.ThrowIfNull(topology);
        var edited = original with { Services = [], Topology = topology };
        edited.Validate(adapters);
        var name = topology.Nodes.Single(node => node.Kind == EnvironmentNodeKind.Host).Name;
        if (name != original.Name) ValidateHostName(name, original);
        var hosts = Infrastructure.ToList();
        hosts[index] = edited with { Name = name, Topology = edited.CreateTopology() };
        return this with { Infrastructure = hosts };
    }

    public EnvironmentDefaults RemoveHost(InfrastructureHost original)
    {
        var index = Infrastructure.FindIndex(host => ReferenceEquals(host, original));
        if (index < 0) return this;
        var hosts = Infrastructure.ToList();
        hosts.RemoveAt(index);
        return this with { Infrastructure = hosts };
    }

    public EnvironmentDefaults AddRegistry() =>
        this with { Registries = [.. Registries, new ImageRegistry { Name = "Docker Hub" }] };

    public EnvironmentDefaults UpdateRegistry(ImageRegistry registry)
    {
        var index = Registries.FindIndex(item => item.Id == registry.Id);
        if (index < 0 || Registries[index] == registry) return this;
        registry.Validate();
        var registries = Registries.ToList();
        registries[index] = registry;
        return this with { Registries = registries };
    }

    public EnvironmentDefaults RemoveRegistry(string registryId)
    {
        var index = Registries.FindIndex(registry => registry.Id == registryId);
        if (index < 0) return this;
        if (Profiles.Any(profile => profile.RegistryId == registryId))
            throw new InvalidOperationException("Change profiles that use this registry before removing it.");

        var registries = Registries.ToList();
        registries.RemoveAt(index);
        return this with { Registries = registries };
    }

    public EnvironmentDefaults AddProfile(EnvironmentProviderDescription provider) =>
        Registries.FirstOrDefault() is { } registry
            ? this with { Profiles = [.. Profiles, ContainerProfileServers.Create(provider, registry)] }
            : this;

    public EnvironmentDefaults UpdateProfile(ContainerProfile original, ContainerProfile edited) =>
        UpdateProfile(original, edited, []);

    // A server change needs the current providers to check the target and its resource type.
    public EnvironmentDefaults UpdateProfile(ContainerProfile original, ContainerProfile edited,
        IEnumerable<EnvironmentProviderDescription> providers)
    {
        var index = Profiles.FindIndex(item => item.Id == original.Id);
        if (index < 0 || edited.Id != original.Id || !ReferenceEquals(Profiles[index], original)) return this;

        // Server selection can release an incompatible default. Only SetDefault assigns one.
        var assignment = original.DefaultForResourceType;
        if (edited.Provider != original.Provider)
        {
            var server = providers.FirstOrDefault(item => item.Name == edited.Provider);
            if (edited.Provider is not null && server is null) return this;
            if (server?.ResourceType != assignment) assignment = null;
        }
        if (edited.DefaultForResourceType != assignment) edited = edited with { DefaultForResourceType = assignment };
        if (edited == original) return this;
        ValidateProfile(edited);
        if (edited == (original with { Settings = edited.Settings }) && JsonNode.DeepEquals(
            JsonSerializer.SerializeToNode(original.Settings, EnvironmentJsonContext.Default.ResourceBinding),
            JsonSerializer.SerializeToNode(edited.Settings, EnvironmentJsonContext.Default.ResourceBinding))) return this;

        var profiles = Profiles.ToList();
        profiles[index] = edited;
        return this with { Profiles = profiles };
    }

    public EnvironmentDefaults RemoveProfile(string profileId)
    {
        var index = Profiles.FindIndex(profile => profile.Id == profileId);
        if (index < 0) return this;
        var profiles = Profiles.ToList();
        profiles.RemoveAt(index);
        return this with { Profiles = profiles };
    }

    public EnvironmentDefaults SetDefault(string profileId, EnvironmentProviderDescription provider)
    {
        var profile = Profiles.Find(item => item.Id == profileId);
        if (profile is null || profile.Provider != provider.Name || profile.DefaultForResourceType == provider.ResourceType) return this;

        return this with { Profiles = Profiles.Select(item => item.Id == profileId
            ? item with { DefaultForResourceType = provider.ResourceType }
            : item.DefaultForResourceType == provider.ResourceType ? item with { DefaultForResourceType = null } : item).ToList() };
    }

    public EnvironmentDefaults ClearDefault(string profileId, EnvironmentProviderDescription provider)
    {
        var index = Profiles.FindIndex(item => item.Id == profileId && item.Provider == provider.Name &&
            item.DefaultForResourceType == provider.ResourceType);
        if (index < 0) return this;
        var profiles = Profiles.ToList();
        profiles[index] = profiles[index] with { DefaultForResourceType = null };
        return this with { Profiles = profiles };
    }

    // Convert older direct image addresses once, retaining their original destination.
    public EnvironmentDefaults WithExplicitRegistries()
    {
        if (Profiles is null || Registries is null) return this;
        var registries = Registries.ToList();
        var profiles = Profiles.Select(profile =>
        {
            if (profile is null || profile.RegistryId is not null || profile.Settings?.Image is not { } image) return profile!;
            var reference = ContainerImageReference.Parse(image);
            var address = reference.Registry ?? "docker.io";
            var registry = registries.FirstOrDefault(item => item?.Address == address);
            if (registry is null)
            {
                registry = new ImageRegistry { Name = address == "docker.io" ? "Docker Hub" : address, Address = address };
                registries.Add(registry);
            }
            return profile with { RegistryId = registry.Id, Settings = profile.Settings with
                { Image = reference.Registry is null ? image : image[(reference.Registry.Length + 1)..] } };
        }).ToList();
        return this with { Registries = registries, Profiles = profiles };
    }

    public void Validate() => Validate(null);

    public void Validate(IEnumerable<IResourceEnvironmentAdapter>? adapters)
    {
        if (Infrastructure is null || Infrastructure.Any(item => item is null) ||
            Infrastructure.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != Infrastructure.Count)
            throw new ArgumentException("Infrastructure hosts require distinct identities.");
        foreach (var host in Infrastructure) host.Validate(adapters);
        if (Registries is null || Profiles is null || Registries.Any(item => item is null) || Profiles.Any(item => item is null))
            throw new ArgumentException("Registries and profiles must be lists.");
        foreach (var registry in Registries) registry.Validate();
        if (Registries.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != Registries.Count ||
            Profiles.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != Profiles.Count)
            throw new ArgumentException("Registry and profile identifiers must be unique.");
        foreach (var profile in Profiles) ValidateProfile(profile);
        var defaults = Profiles.Where(item => item.DefaultForResourceType is not null).Select(item => item.DefaultForResourceType).ToArray();
        if (defaults.Distinct(StringComparer.Ordinal).Count() != defaults.Length) throw new ArgumentException("Choose one default profile per resource type.");
    }

    private void ValidateProfile(ContainerProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Id) || string.IsNullOrWhiteSpace(profile.Name) || profile.Settings is null)
            throw new ArgumentException("A profile name and settings are required.");
        _ = profile.ResolveImage(Registries);
        new EnvironmentProfile { Resources = new() { [profile.Id] = profile.Settings } }.Validate();
        if (profile.DefaultForResourceType is not null && string.IsNullOrWhiteSpace(profile.Provider))
            throw new ArgumentException("Image-only profiles cannot be resource defaults.");
    }
}
