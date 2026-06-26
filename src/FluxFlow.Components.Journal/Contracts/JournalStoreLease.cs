namespace FluxFlow.Components.Journal.Contracts;

public sealed class JournalStoreLease : IAsyncDisposable
{
    private bool _disposed;

    private JournalStoreLease(IJournalStore store, bool ownsStore)
    {
        Store = store ?? throw new ArgumentNullException(nameof(store));
        OwnsStore = ownsStore;
    }

    public IJournalStore Store { get; }

    public bool OwnsStore { get; }

    public static JournalStoreLease Owned(IJournalStore store)
        => new(store, ownsStore: true);

    public static JournalStoreLease Shared(IJournalStore store)
        => new(store, ownsStore: false);

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        if (!OwnsStore)
            return;

        if (Store is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
            return;
        }

        if (Store is IDisposable disposable)
            disposable.Dispose();
    }
}
