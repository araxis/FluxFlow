using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Components.Secrets;

public static class SecretServiceCollectionExtensions
{
    public static IServiceCollection AddFluxFlowSecretResolver(
        this IServiceCollection services,
        string name,
        ISecretResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(resolver);

        return services.AddFluxFlowSecretResolver(name, _ => resolver);
    }

    public static IServiceCollection AddFluxFlowSecretResolver(
        this IServiceCollection services,
        string name,
        Func<IServiceProvider, ISecretResolver> resolverFactory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(resolverFactory);

        var normalizedName = name.Trim();

        services.AddKeyedSingleton<ISecretResolver>(
            normalizedName,
            (provider, _) => resolverFactory(provider)
                ?? throw new InvalidOperationException("Secret resolver factory returned null."));

        return services;
    }

    public static IServiceCollection AddFluxFlowSecretDescriptorProvider(
        this IServiceCollection services,
        string name,
        ISecretDescriptorProvider descriptorProvider)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(descriptorProvider);

        return services.AddFluxFlowSecretDescriptorProvider(name, _ => descriptorProvider);
    }

    public static IServiceCollection AddFluxFlowSecretDescriptorProvider(
        this IServiceCollection services,
        string name,
        Func<IServiceProvider, ISecretDescriptorProvider> descriptorProviderFactory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(descriptorProviderFactory);

        var normalizedName = name.Trim();

        services.AddKeyedSingleton<ISecretDescriptorProvider>(
            normalizedName,
            (provider, _) => descriptorProviderFactory(provider)
                ?? throw new InvalidOperationException("Secret descriptor provider factory returned null."));

        return services;
    }
}
