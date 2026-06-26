using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Components.Resources;

public static class ResourceServiceCollectionExtensions
{
    public static IServiceCollection AddFluxFlowResourceLookup(
        this IServiceCollection services,
        string name,
        IResourceLookup lookup)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(lookup);

        return services.AddFluxFlowResourceLookup(name, _ => lookup);
    }

    public static IServiceCollection AddFluxFlowResourceLookup(
        this IServiceCollection services,
        string name,
        Func<IServiceProvider, IResourceLookup> lookupFactory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(lookupFactory);

        var normalizedName = name.Trim();

        services.AddKeyedSingleton<IResourceLookup>(
            normalizedName,
            (provider, _) => lookupFactory(provider)
                ?? throw new InvalidOperationException("Resource lookup factory returned null."));
        services.AddKeyedSingleton<IResourceDescriptorProvider>(
            normalizedName,
            (provider, _) => provider.GetRequiredKeyedService<IResourceLookup>(normalizedName));

        return services;
    }

    public static IServiceCollection AddFluxFlowResourceDescriptorProvider(
        this IServiceCollection services,
        string name,
        IResourceDescriptorProvider descriptorProvider)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(descriptorProvider);

        return services.AddFluxFlowResourceDescriptorProvider(name, _ => descriptorProvider);
    }

    public static IServiceCollection AddFluxFlowResourceDescriptorProvider(
        this IServiceCollection services,
        string name,
        Func<IServiceProvider, IResourceDescriptorProvider> descriptorProviderFactory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(descriptorProviderFactory);

        var normalizedName = name.Trim();

        services.AddKeyedSingleton<IResourceDescriptorProvider>(
            normalizedName,
            (provider, _) => descriptorProviderFactory(provider)
                ?? throw new InvalidOperationException("Resource descriptor provider factory returned null."));

        return services;
    }
}
