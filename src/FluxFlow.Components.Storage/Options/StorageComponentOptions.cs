using FluxFlow.Components.Storage.Contracts;

namespace FluxFlow.Components.Storage.Options;

public sealed class StorageComponentOptions
{
    private IStorageStoreFactory _storeFactory = new MissingStorageStoreFactory();

    public IStorageStoreFactory StoreFactory => _storeFactory;

    public StorageComponentOptions UseStoreFactory(IStorageStoreFactory storeFactory)
    {
        _storeFactory = storeFactory ?? throw new ArgumentNullException(nameof(storeFactory));
        return this;
    }

    public StorageComponentOptions UseStore(
        Func<StorageStoreContext, CancellationToken, ValueTask<StorageStoreLease>> open)
    {
        ArgumentNullException.ThrowIfNull(open);
        _storeFactory = new DelegateStorageStoreFactory(open);
        return this;
    }

    public StorageComponentOptions UseSharedStore(Func<StorageStoreContext, IStorageStore> create)
    {
        ArgumentNullException.ThrowIfNull(create);
        return UseStore(
            (context, _) => ValueTask.FromResult(StorageStoreLease.Shared(create(context))));
    }

    public StorageComponentOptions UseSharedStore(IStorageStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        return UseStore(
            (_, _) => ValueTask.FromResult(StorageStoreLease.Shared(store)));
    }

    private sealed class DelegateStorageStoreFactory(
        Func<StorageStoreContext, CancellationToken, ValueTask<StorageStoreLease>> open)
        : IStorageStoreFactory
    {
        public ValueTask<StorageStoreLease> OpenAsync(
            StorageStoreContext context,
            CancellationToken cancellationToken = default)
            => open(context, cancellationToken);
    }

    private sealed class MissingStorageStoreFactory : IStorageStoreFactory
    {
        public ValueTask<StorageStoreLease> OpenAsync(
            StorageStoreContext context,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException(
                "Storage components require a storage store. Register one through StorageComponentOptions.");
    }
}
