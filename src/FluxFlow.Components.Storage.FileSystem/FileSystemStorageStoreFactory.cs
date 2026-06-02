using FluxFlow.Components.Storage.Contracts;

namespace FluxFlow.Components.Storage.FileSystem;

public sealed class FileSystemStorageStoreFactory : IStorageStoreFactory
{
    private readonly FileSystemStorageStoreOptions _options;

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

        var store = new FileSystemStorageStore(_options, context);
        return ValueTask.FromResult(StorageStoreLease.Owned(store));
    }
}
