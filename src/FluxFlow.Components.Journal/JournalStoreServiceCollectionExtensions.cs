using FluxFlow.Components.Journal.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Components.Journal;

public static class JournalStoreServiceCollectionExtensions
{
    public static IServiceCollection AddFluxFlowJournalStore(
        this IServiceCollection services,
        string name,
        IJournalStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        return services.AddFluxFlowJournalStore(name, _ => store);
    }

    public static IServiceCollection AddFluxFlowJournalStore(
        this IServiceCollection services,
        string name,
        Func<IServiceProvider, IJournalStore> storeFactory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(storeFactory);

        services.AddKeyedSingleton<IJournalStore>(
            name,
            (provider, _) => storeFactory(provider)
                ?? throw new InvalidOperationException("Journal store provider returned null."));

        return services;
    }

    public static IServiceCollection AddFluxFlowJournalStoreFactory(
        this IServiceCollection services,
        string name,
        IJournalStoreFactory storeFactory)
    {
        ArgumentNullException.ThrowIfNull(storeFactory);
        return services.AddFluxFlowJournalStoreFactory(name, _ => storeFactory);
    }

    public static IServiceCollection AddFluxFlowJournalStoreFactory(
        this IServiceCollection services,
        string name,
        Func<IServiceProvider, IJournalStoreFactory> storeFactory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(storeFactory);

        services.AddKeyedSingleton<IJournalStoreFactory>(
            name,
            (provider, _) => storeFactory(provider)
                ?? throw new InvalidOperationException("Journal store factory provider returned null."));

        return services;
    }
}
