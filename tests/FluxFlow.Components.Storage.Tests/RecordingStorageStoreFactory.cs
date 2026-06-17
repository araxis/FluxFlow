using FluxFlow.Components.Storage.Contracts;

namespace FluxFlow.Components.Storage.Tests;

/// <summary>
/// Records every OpenAsync call. By default it hands out a single shared store
/// instance (so a reconnect reuses the same store) as an Owned lease so disconnect
/// disposes it; pass <paramref name="ownLease"/> false to model a host-owned
/// (Shared) store that must not be disposed.
/// </summary>
internal sealed class RecordingStorageStoreFactory : IStorageStoreFactory
{
    private readonly object _gate = new();
    private readonly List<StorageStoreContext> _contexts = [];
    private readonly InMemoryStorageStore _store;
    private readonly bool _ownLease;

    private int _openCalls;

    public RecordingStorageStoreFactory(InMemoryStorageStore store, bool ownLease = true)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _ownLease = ownLease;
    }

    public int OpenCalls => Volatile.Read(ref _openCalls);

    public InMemoryStorageStore Store => _store;

    public IReadOnlyList<StorageStoreContext> Contexts
    {
        get
        {
            lock (_gate)
            {
                return _contexts.ToArray();
            }
        }
    }

    public ValueTask<StorageStoreLease> OpenAsync(
        StorageStoreContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _openCalls);
        lock (_gate)
        {
            _contexts.Add(context);
        }

        return ValueTask.FromResult(_ownLease
            ? StorageStoreLease.Owned(_store)
            : StorageStoreLease.Shared(_store));
    }
}

/// <summary>
/// Parks every OpenAsync inside a caller-supplied gate so a test can deterministically
/// observe two concurrent ConnectAsync calls collapsing onto a single in-flight open.
/// </summary>
internal sealed class GatedStorageStoreFactory : IStorageStoreFactory
{
    private readonly InMemoryStorageStore _store;
    private readonly Task _gate;
    private int _openCalls;

    public GatedStorageStoreFactory(InMemoryStorageStore store, Task gate)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
    }

    public int OpenCalls => Volatile.Read(ref _openCalls);

    public async ValueTask<StorageStoreLease> OpenAsync(
        StorageStoreContext context,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _openCalls);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        return StorageStoreLease.Owned(_store);
    }
}
