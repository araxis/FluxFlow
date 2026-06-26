using FluxFlow.Components.Storage.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Components.Storage.SqlFile;

public static class SqlFileStorageServiceCollectionExtensions
{
    public static IServiceCollection AddFluxFlowSqlFileStorageStoreFactory(
        this IServiceCollection services,
        string name,
        SqlFileStorageStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return services.AddFluxFlowSqlFileStorageStoreFactory(name, _ => options);
    }

    public static IServiceCollection AddFluxFlowSqlFileStorageStoreFactory(
        this IServiceCollection services,
        string name,
        Func<IServiceProvider, SqlFileStorageStoreOptions> optionsFactory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(optionsFactory);

        services.AddKeyedSingleton<IStorageStoreFactory>(
            name,
            (provider, _) => new SqlFileStorageStoreFactory(optionsFactory(provider)));

        return services;
    }
}
