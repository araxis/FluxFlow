using FluxFlow.Components.Storage.Contracts;
using System.Collections.Concurrent;

namespace FluxFlow.Components.Storage.FileSystem;

public sealed class FileSystemStorageStoreFactory : IStorageStoreFactory
{
    private readonly FileSystemStorageStoreOptions _options;
    private readonly ConcurrentDictionary<StoreKey, FileSystemStorageStore> _stores = [];

    public FileSystemStorageStoreFactory(FileSystemStorageStoreOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public ValueTask<StorageStoreLease> OpenAsync(
        StorageStoreContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        var settings = _options.Resolve(context);
        var store = _stores.GetOrAdd(
            CreateStoreKey(settings),
            _ => new FileSystemStorageStore(settings));
        return ValueTask.FromResult(StorageStoreLease.Shared(store));
    }

    private static StoreKey CreateStoreKey(FileSystemStorageStoreSettings settings)
        => new(
            settings.RootDirectory.ToUpperInvariant(),
            settings.StoreName,
            settings.DefaultCollection,
            settings.Clock);

    private sealed record StoreKey(
        string RootDirectory,
        string StoreName,
        string? DefaultCollection,
        TimeProvider Clock);
}
