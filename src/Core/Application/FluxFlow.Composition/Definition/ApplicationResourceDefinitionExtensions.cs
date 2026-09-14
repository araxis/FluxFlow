namespace FluxFlow.Composition.Definition;

public static class ApplicationResourceTypes
{
    public const string External = "host.external";
}

public static class ApplicationResourceDefinitionExtensions
{
    public static ResourceHandle<TResource> AddExternalResource<TResource>(
        this ApplicationDefinitionBuilder resources,
        string name)
    {
        ArgumentNullException.ThrowIfNull(resources);
        return resources.AddResource<TResource>(
            name,
            ApplicationResourceTypes.External);
    }

    public static ApplicationDefinitionBuilder AddExternalResource<TResource>(
        this ApplicationDefinitionBuilder application,
        string name,
        out ResourceHandle<TResource> resource)
    {
        ArgumentNullException.ThrowIfNull(application);
        resource = application.AddExternalResource<TResource>(name);
        return application;
    }

}
