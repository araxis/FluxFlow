namespace FluxFlow.Composition.Authoring;

public static class ApplicationResourceTypes
{
    public const string External = "host.external";
}

public static class ApplicationResourceAuthoringExtensions
{
    public static ResourceHandle<TResource> AddExternalResource<TResource>(
        this IResourceDefinitionContainerBuilder resources,
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

    public static ResourceGroupBuilder AddExternalResource<TResource>(
        this ResourceGroupBuilder group,
        string name,
        out ResourceHandle<TResource> resource)
    {
        ArgumentNullException.ThrowIfNull(group);
        resource = group.AddExternalResource<TResource>(name);
        return group;
    }

    public static IResourceDefinitionContainerBuilder AddExternalResource<TResource>(
        this IResourceDefinitionContainerBuilder resources,
        string name,
        out ResourceHandle<TResource> resource)
    {
        ArgumentNullException.ThrowIfNull(resources);
        resource = resources.AddExternalResource<TResource>(name);
        return resources;
    }
}
