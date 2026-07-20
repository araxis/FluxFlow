using FluxFlow.Composition.Addressing;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Components.Secrets;

public static class SecretServiceCollectionExtensions
{
    public static IServiceCollection AddFluxFlowSecretResolver(
        this IServiceCollection services,
        ApplicationAddress address,
        Func<IServiceProvider, ISecretResolver> resolverFactory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ValidateResourceAddress(address);
        ArgumentNullException.ThrowIfNull(resolverFactory);

        services.AddKeyedSingleton<ISecretResolver>(
            address.Value,
            (provider, _) => resolverFactory(provider)
                ?? throw new InvalidOperationException("Secret resolver factory returned null."));

        return services;
    }

    public static IServiceCollection AddExternalFluxFlowSecretResolver(
        this IServiceCollection services,
        ApplicationAddress address,
        ISecretResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(services);
        ValidateResourceAddress(address);
        ArgumentNullException.ThrowIfNull(resolver);
        services.AddKeyedSingleton(address.Value, resolver);
        return services;
    }

    public static IServiceCollection AddFluxFlowSecretDescriptorProvider(
        this IServiceCollection services,
        ApplicationAddress address,
        Func<IServiceProvider, ISecretDescriptorProvider> descriptorProviderFactory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ValidateResourceAddress(address);
        ArgumentNullException.ThrowIfNull(descriptorProviderFactory);

        services.AddKeyedSingleton<ISecretDescriptorProvider>(
            address.Value,
            (provider, _) => descriptorProviderFactory(provider)
                ?? throw new InvalidOperationException("Secret descriptor provider factory returned null."));

        return services;
    }

    public static IServiceCollection AddExternalFluxFlowSecretDescriptorProvider(
        this IServiceCollection services,
        ApplicationAddress address,
        ISecretDescriptorProvider descriptorProvider)
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
}
