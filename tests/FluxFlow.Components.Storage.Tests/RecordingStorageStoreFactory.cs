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
/// Signals when each OpenAsync has been entered (so a test can race a
/// disconnect/dispose against an in-flight open before releasing the gate). The gate
/// is awaited with CancellationToken.None so a cancelled connect token cannot short
/// the open before the racing teardown runs.
/// </summary>
internal sealed class GatedStorageStoreFactory : IStorageStoreFactory
{
    private readonly InMemoryStorageStore _store;
    private readonly Task _gate;
    private readonly TaskCompletionSource _opened;
    private int _openCalls;

    public GatedStorageStoreFactory(InMemoryStorageStore store, Task gate)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
        _opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public int OpenCalls => Volatile.Read(ref _openCalls);

    /// <summary>Completes once OpenAsync has been entered (before the gate is awaited).</summary>
    public Task Opened => _opened.Task;

    public async ValueTask<StorageStoreLease> OpenAsync(
        StorageStoreContext context,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _openCalls);
        _opened.TrySetResult();

        // Await with None so the in-flight open survives a cancelled connect token long
        // enough for the racing disconnect/dispose to run and the publish guard to drop
        // the fresh lease (the leak/teardown vectors under test), mirroring the shipped
        // FileSystem/SqlFile factories that only observe cancellation at entry.
        await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        return StorageStoreLease.Owned(_store);
    }
}

/// <summary>
/// Throws from OpenAsync until the configured number of failures is exhausted, then
/// hands out the store as an Owned lease. Models a transient connect fault that is
/// distinct from the missing-factory case, so a retry can succeed.
/// </summary>
internal sealed class FaultThenSucceedStorageStoreFactory : IStorageStoreFactory
{
    private readonly InMemoryStorageStore _store;
    private int _remainingFaults;
    private int _openCalls;

    public FaultThenSucceedStorageStoreFactory(InMemoryStorageStore store, int faults = 1)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _remainingFaults = faults;
    }

    public int OpenCalls => Volatile.Read(ref _openCalls);

    public ValueTask<StorageStoreLease> OpenAsync(
        StorageStoreContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _openCalls);

        if (Interlocked.Decrement(ref _remainingFaults) >= 0)
        {
            throw new InvalidOperationException("Storage store failed to open (transient).");
        }

        return ValueTask.FromResult(StorageStoreLease.Owned(_store));
    }
}
