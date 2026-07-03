using FluxFlow.Components.Sessions.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Components.Sessions;

public static class SessionStoreServiceCollectionExtensions
{
    public static IServiceCollection AddFluxFlowSessionStore(
        this IServiceCollection services,
        string name,
        ISessionStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        return services.AddFluxFlowSessionStore(name, _ => store);
    }

    public static IServiceCollection AddFluxFlowSessionStore(
        this IServiceCollection services,
        string name,
        Func<IServiceProvider, ISessionStore> storeFactory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(storeFactory);

        var normalizedName = name.Trim();

        services.AddKeyedSingleton<ISessionStore>(
            normalizedName,
            (provider, _) => storeFactory(provider)
                ?? throw new InvalidOperationException("Session store provider returned null."));

        return services;
    }

    public static IServiceCollection AddFluxFlowSessionStoreFactory(
        this IServiceCollection services,
        string name,
        ISessionStoreFactory storeFactory)
    {
        ArgumentNullException.ThrowIfNull(storeFactory);
        return services.AddFluxFlowSessionStoreFactory(name, _ => storeFactory);
    }

    public static IServiceCollection AddFluxFlowSessionStoreFactory(
        this IServiceCollection services,
        string name,
        Func<IServiceProvider, ISessionStoreFactory> storeFactory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(storeFactory);

        var normalizedName = name.Trim();

        services.AddKeyedSingleton<ISessionStoreFactory>(
            normalizedName,
            (provider, _) => storeFactory(provider)
                ?? throw new InvalidOperationException("Session store factory provider returned null."));

        return services;
    }
}
