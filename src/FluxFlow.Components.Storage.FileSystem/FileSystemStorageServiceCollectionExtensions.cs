using FluxFlow.Components.Storage.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Components.Storage.FileSystem;

public static class FileSystemStorageServiceCollectionExtensions
{
    public static IServiceCollection AddFluxFlowFileSystemStorageStore(
        this IServiceCollection services,
        string name,
        FileSystemStorageStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return services.AddFluxFlowFileSystemStorageStore(name, _ => options);
    }

    public static IServiceCollection AddFluxFlowFileSystemStorageStore(
        this IServiceCollection services,
        string name,
        Func<IServiceProvider, FileSystemStorageStoreOptions> optionsFactory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(optionsFactory);

        services.AddKeyedSingleton<IStorageStore>(
            name,
            (provider, _) => new FileSystemStorageStore(optionsFactory(provider)));

        return services;
    }

    public static IServiceCollection AddFluxFlowFileSystemStorageStoreFactory(
        this IServiceCollection services,
        string name,
        FileSystemStorageStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return services.AddFluxFlowFileSystemStorageStoreFactory(name, _ => options);
    }

    public static IServiceCollection AddFluxFlowFileSystemStorageStoreFactory(
        this IServiceCollection services,
        string name,
        Func<IServiceProvider, FileSystemStorageStoreOptions> optionsFactory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(optionsFactory);

        services.AddKeyedSingleton<IStorageStoreFactory>(
            name,
            (provider, _) => new FileSystemStorageStoreFactory(optionsFactory(provider)));

        return services;
    }
}
