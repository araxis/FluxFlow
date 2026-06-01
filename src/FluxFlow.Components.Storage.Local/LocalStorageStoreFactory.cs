using FluxFlow.Components.Storage.Contracts;

namespace FluxFlow.Components.Storage.Local;

public sealed class LocalStorageStoreFactory : IStorageStoreFactory
{
    private readonly LocalStorageStoreOptions _options;

    public LocalStorageStoreFactory(LocalStorageStoreOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public ValueTask<StorageStoreLease> OpenAsync(
        StorageStoreContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        var store = new LocalStorageStore(_options, context);
        return ValueTask.FromResult(StorageStoreLease.Owned(store));
    }
}
