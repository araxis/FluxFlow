namespace FluxFlow.Components.Sessions.Contracts;

public sealed class SessionStoreLease : IAsyncDisposable
{
    private bool _disposed;

    private SessionStoreLease(ISessionStore store, bool ownsStore)
    {
        Store = store ?? throw new ArgumentNullException(nameof(store));
        OwnsStore = ownsStore;
    }

    public ISessionStore Store { get; }

    public bool OwnsStore { get; }

    public static SessionStoreLease Owned(ISessionStore store)
        => new(store, ownsStore: true);

    public static SessionStoreLease Shared(ISessionStore store)
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
