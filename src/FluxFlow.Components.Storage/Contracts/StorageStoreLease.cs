namespace FluxFlow.Components.Storage.Contracts;

public sealed class StorageStoreLease : IAsyncDisposable
{
    private bool _disposed;

    private StorageStoreLease(IStorageStore store, bool ownsStore)
    {
        Store = store ?? throw new ArgumentNullException(nameof(store));
        OwnsStore = ownsStore;
    }

    public IStorageStore Store { get; }

    public bool OwnsStore { get; }

    public static StorageStoreLease Owned(IStorageStore store)
        => new(store, ownsStore: true);

    public static StorageStoreLease Shared(IStorageStore store)
        => new(store, ownsStore: false);

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (!OwnsStore)
        {
            return;
        }

        if (Store is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
            return;
        }

        if (Store is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
