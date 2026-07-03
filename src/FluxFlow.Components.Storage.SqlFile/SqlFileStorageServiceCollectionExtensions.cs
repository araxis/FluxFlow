using FluxFlow.Components.Storage.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Components.Storage.SqlFile;

public static class SqlFileStorageServiceCollectionExtensions
{
    public static IServiceCollection AddFluxFlowSqlFileStorageStore(
        this IServiceCollection services,
        string name,
        SqlFileStorageStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return services.AddFluxFlowSqlFileStorageStore(name, _ => options);
    }

    public static IServiceCollection AddFluxFlowSqlFileStorageStore(
        this IServiceCollection services,
        string name,
        Func<IServiceProvider, SqlFileStorageStoreOptions> optionsFactory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(optionsFactory);

        var normalizedName = name.Trim();

        services.AddKeyedSingleton<IStorageStore>(
            normalizedName,
            (provider, _) => new SqlFileStorageStore(
                optionsFactory(provider)
                    ?? throw new InvalidOperationException(
                        "SQL-file storage options factory returned null.")));

        return services;
    }

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

        var normalizedName = name.Trim();

        services.AddKeyedSingleton<IStorageStoreFactory>(
            normalizedName,
            (provider, _) => new SqlFileStorageStoreFactory(
                optionsFactory(provider)
                    ?? throw new InvalidOperationException(
                        "SQL-file storage options factory returned null.")));

        return services;
    }
}
