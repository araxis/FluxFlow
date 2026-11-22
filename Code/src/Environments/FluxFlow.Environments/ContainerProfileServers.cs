namespace FluxFlow.Environments;

/// <summary>Server choices and edits for a reusable resource profile.</summary>
public sealed class ContainerProfileServers(ContainerProfile profile, IEnumerable<EnvironmentProviderDescription> providers)
{
    public EnvironmentProviderDescription? Current { get; } = providers.FirstOrDefault(item => item.Name == profile.Provider);
    public IEnumerable<EnvironmentProviderDescription> Servers => providers.Where(item => item.ResourceType == Current?.ResourceType);

    // Resource defaults stores the registry separately and starts new profiles at :latest.
    private static string StockImage(EnvironmentProviderDescription provider) =>
        ContainerImageReference.Parse(provider.Image).Name + ":latest";

    public static ContainerProfile Create(EnvironmentProviderDescription provider, ImageRegistry registry) => new()
    {
        RegistryId = registry.Id, Provider = provider.Name,
        Settings = new() { Provider = provider.Name, Image = StockImage(provider) }
    };

    public ContainerProfile SelectServer(string name) => Select(Servers.FirstOrDefault(item => item.Name == name));

    public ContainerProfile SelectConnectionType(string type) => type == Current?.ResourceType ? profile :
        Select(providers.FirstOrDefault(item => item.ResourceType == type));

    private ContainerProfile Select(EnvironmentProviderDescription? next)
    {
        if (next is null || next.Name == profile.Provider || profile.Settings.Profile?.SetupLocked == true) return profile;
        var image = profile.Settings.Image;
        return profile with
        {
            Provider = next.Name,
            DefaultForResourceType = profile.DefaultForResourceType == next.ResourceType ? next.ResourceType : null,
            Settings = profile.Settings with { Provider = next.Name, Image = Current is not null && image == StockImage(Current) ? StockImage(next) : image }
        };
    }
}
