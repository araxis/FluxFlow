using FluxFlow.Composition.Addressing;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Components.Resources;

public static class ResourceServiceCollectionExtensions
{
    public static IServiceCollection AddFluxFlowResourceLookup(
        this IServiceCollection services,
        ApplicationAddress address,
        Func<IServiceProvider, IResourceLookup> lookupFactory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ValidateResourceAddress(address);
        ArgumentNullException.ThrowIfNull(lookupFactory);

        var key = address.Value;

        services.AddKeyedSingleton<IResourceLookup>(
            key,
            (provider, _) => lookupFactory(provider)
                ?? throw new InvalidOperationException("Resource lookup factory returned null."));
        services.AddKeyedSingleton<IResourceDescriptorProvider>(
            key,
            (provider, _) => new ResourceLookupDescriptorProviderView(
                provider.GetRequiredKeyedService<IResourceLookup>(key)));

        return services;
    }

    public static IServiceCollection AddExternalFluxFlowResourceLookup(
        this IServiceCollection services,
        ApplicationAddress address,
        IResourceLookup lookup)
    {
        ArgumentNullException.ThrowIfNull(services);
        ValidateResourceAddress(address);
        ArgumentNullException.ThrowIfNull(lookup);
        var key = address.Value;
        services.AddKeyedSingleton<IResourceLookup>(key, lookup);
        services.AddKeyedSingleton<IResourceDescriptorProvider>(
            key,
            (_, _) => new ResourceLookupDescriptorProviderView(lookup));
        return services;
    }

    public static IServiceCollection AddFluxFlowResourceDescriptorProvider(
        this IServiceCollection services,
        ApplicationAddress address,
        Func<IServiceProvider, IResourceDescriptorProvider> descriptorProviderFactory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ValidateResourceAddress(address);
        ArgumentNullException.ThrowIfNull(descriptorProviderFactory);

        services.AddKeyedSingleton<IResourceDescriptorProvider>(
            address.Value,
            (provider, _) => descriptorProviderFactory(provider)
                ?? throw new InvalidOperationException("Resource descriptor provider factory returned null."));

        return services;
    }

    public static IServiceCollection AddExternalFluxFlowResourceDescriptorProvider(
        this IServiceCollection services,
        ApplicationAddress address,
        IResourceDescriptorProvider descriptorProvider)
    {
        ArgumentNullException.ThrowIfNull(services);
        ValidateResourceAddress(address);
        ArgumentNullException.ThrowIfNull(descriptorProvider);
        services.AddKeyedSingleton(address.Value, descriptorProvider);
        return services;
    }

    private static void ValidateResourceAddress(ApplicationAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (address.Kind != ApplicationAddressKind.Resource)
        {
            throw new ArgumentException(
                $"Address '{address}' must be a resource address.",
                nameof(address));
        }
    }

    private sealed class ResourceLookupDescriptorProviderView(
        IResourceDescriptorProvider descriptorProvider) : IResourceDescriptorProvider
    {
        public IReadOnlyCollection<Contracts.ResourceDescriptor> GetResources()
            => descriptorProvider.GetResources();
    }
}
