using System.Diagnostics.CodeAnalysis;
using FluxFlow.Components.Storage.Contracts;
using FluxFlow.Components.Storage.Diagnostics;
using FluxFlow.Engine.Components;
using FluxFlow.Engine.Definitions;

namespace FluxFlow.Components.Storage.Nodes;

/// <summary>
/// Owns the storage store lifecycle. Opening and closing the store is an explicit,
/// host-API-only decision: there is no auto-open, no lazy open, and no in-graph
/// command port. Put/Get/Query/Delete nodes borrow the opened store via
/// <see cref="TryGetStore"/> and never open or dispose it.
/// </summary>
public sealed class StorageStoreNode : FlowNodeBase, IStorageStoreHandle, IAsyncDisposable
{
    private readonly NodeAddress _address;
    private readonly IStorageStoreFactory _storeFactory;
    private readonly TimeProvider _clock;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _lifecycleCancellation = new();

    private volatile IStorageStore? _store;
    private volatile StorageStoreConnectionState _state = StorageStoreConnectionState.Disconnected;

    private StorageStoreLease? _lease;
    private Task<StorageStoreLease>? _inFlightConnect;
    private CancellationTokenSource? _connectCts;
    private bool _userDisconnected;
    private bool _disposed;

    internal StorageStoreNode(
        NodeAddress address,
        string storeName,
        IStorageStoreFactory storeFactory,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentException.ThrowIfNullOrWhiteSpace(storeName);
        ArgumentNullException.ThrowIfNull(storeFactory);
        ArgumentNullException.ThrowIfNull(clock);

        _address = address;
        StoreName = storeName;
        _storeFactory = storeFactory;
        _clock = clock;
    }

    public string StoreName { get; }

    public StorageStoreConnectionState State => _state;

    // StartAsync stays a no-op: opening the store is an explicit host decision.

    public bool TryGetStore([NotNullWhen(true)] out IStorageStore? store)
    {
        // Lock-free borrow. Read state first, then the store, so a concurrent
        // disconnect (which clears the store before flipping state) can only ever
        // cause a false negative, never a borrow of a torn-down store.
        if (_state == StorageStoreConnectionState.Connected)
        {
            var current = _store;
            if (current is not null)
            {
                store = current;
                return true;
            }
        }

        store = null;
        return false;
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        // Idempotent fast path: already connected.
        if (_state == StorageStoreConnectionState.Connected)
        {
            return;
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        Task<StorageStoreLease> connect;
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_state == StorageStoreConnectionState.Connected)
            {
                return;
            }

            // Single-flight: if an open is already running, capture it, release the
            // gate, and await it instead of starting a second OpenAsync.
            if (_inFlightConnect is not null)
            {
                connect = _inFlightConnect;
            }
            else
            {
                _userDisconnected = false;

                // Dispose the previous connect CTS before creating a new one so a
                // sequence of connects cannot leak cancellation sources.
                _connectCts?.Dispose();
                _connectCts = CancellationTokenSource.CreateLinkedTokenSource(
                    _lifecycleCancellation.Token);

                _state = StorageStoreConnectionState.Connecting;
                connect = EstablishAsync(_connectCts, _connectCts.Token);
                _inFlightConnect = connect;
            }
        }
        finally
        {
            _gate.Release();
        }

        await connect.ConfigureAwait(false);
    }

    private async Task<StorageStoreLease> EstablishAsync(
        CancellationTokenSource ownCts,
        CancellationToken ct)
    {
        // Yield off the gate-holding caller so the in-flight Task is observable to a
        // concurrent ConnectAsync before OpenAsync runs.
        await Task.Yield();

        StorageStoreLease? lease = null;
        try
        {
            ct.ThrowIfCancellationRequested();

            var context = new StorageStoreContext
            {
                Address = _address,
                NodeType = StorageComponentTypes.Store,
                StoreName = StoreName,
                Collection = null,
                Clock = _clock
            };

            lease = await _storeFactory.OpenAsync(context, ct).ConfigureAwait(false);
            ArgumentNullException.ThrowIfNull(lease);

            // Re-acquire the gate to publish. DisposeAsync may have disposed the gate
            // while OpenAsync was running, in which case WaitAsync throws
            // ObjectDisposedException; treat that as "cannot publish" and fall through
            // to robust disposal of the freshly opened lease so it never leaks.
            var published = false;
            try
            {
                await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
                try
                {
                    // Honor any teardown that won the race while we were opening, drop
                    // a cancelled connect, and never overwrite a live store: a
                    // disconnect/dispose, a cancelled token, a superseding establish
                    // (no longer the current in-flight Task), or an already-published
                    // store all mean this fresh lease must be dropped, not published.
                    if (!_userDisconnected &&
                        !_disposed &&
                        !ct.IsCancellationRequested &&
                        ReferenceEquals(_connectCts, ownCts) &&
                        _store is null)
                    {
                        // Publish order: store FIRST, then flip state to Connected LAST
                        // so a borrow that observes Connected always sees a non-null
                        // store.
                        _lease = lease;
                        _store = lease.Store;
                        _state = StorageStoreConnectionState.Connected;
                        _inFlightConnect = null;
                        published = true;
                    }
                }
                finally
                {
                    _gate.Release();
                }
            }
            catch (ObjectDisposedException)
            {
                // Gate disposed by a concurrent DisposeAsync; cannot publish.
            }

            if (!published)
            {
                // Could not publish (teardown/cancel/supersede/already-live store):
                // dispose the fresh lease and return WITHOUT publishing.
                await lease.DisposeAsync().ConfigureAwait(false);
                return lease;
            }

            TryEmitDiagnostic(
                StorageDiagnosticNames.StoreOpened,
                message: $"Opened storage store '{StoreName}'.",
                attributes: CreateStoreAttributes());
            return lease;
        }
        catch (Exception exception)
        {
            // Cancellation here is a requested disconnect/dispose, not a fault: do
            // not clobber the Disconnected state or emit an open-failed diagnostic.
            var cancelled = exception is OperationCanceledException &&
                (ct.IsCancellationRequested || _lifecycleCancellation.IsCancellationRequested);

            // Mutating shared state needs the gate, but a concurrent DisposeAsync may
            // have disposed it (ObjectDisposedException). Tolerate that and STILL
            // dispose the freshly opened lease afterwards so it never leaks.
            try
            {
                await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
                try
                {
                    // Only retract our own in-flight marker; a superseding establish
                    // may already own it. Never clobber a live store: only fault when
                    // this is still the current connect and nothing was published.
                    if (ReferenceEquals(_connectCts, ownCts))
                    {
                        _inFlightConnect = null;
                    }

                    if (!cancelled && !_userDisconnected && !_disposed &&
                        ReferenceEquals(_connectCts, ownCts) && _store is null)
                    {
                        _lease = null;
                        _state = StorageStoreConnectionState.Faulted;
                    }
                }
                finally
                {
                    _gate.Release();
                }
            }
            catch (ObjectDisposedException)
            {
                // Gate disposed by a concurrent DisposeAsync; state is already torn
                // down. Fall through to dispose the half-open lease below.
            }

            // Never leave a half-open lease behind, even if the gate was disposed.
            if (lease is not null)
            {
                await lease.DisposeAsync().ConfigureAwait(false);
            }

            // The default MissingStorageStoreFactory throws here. Surface it as a
            // StoreOpenFailed diagnostic and rethrow so the host observes it; the
            // resource node is never faulted and the runtime is never taken down.
            if (!cancelled && !_userDisconnected && !_disposed)
            {
                TryEmitDiagnostic(
                    StorageDiagnosticNames.StoreOpenFailed,
                    FlowDiagnosticLevel.Error,
                    $"Storage store '{StoreName}' failed to open.",
                    exception,
                    CreateStoreAttributes());
            }

            throw;
        }
    }

    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        StorageStoreLease? lease;
        try
        {
            _userDisconnected = true;
            _connectCts?.Cancel();
            _inFlightConnect = null;

            // Clear the store FIRST so borrows immediately observe not-connected,
            // and flip state so TryGetStore returns false even before teardown.
            Interlocked.Exchange(ref _store, null);
            lease = _lease;
            _lease = null;

            _state = StorageStoreConnectionState.Disconnected;
        }
        finally
        {
            _gate.Release();
        }

        if (lease is not null)
        {
            // Idempotent; honors Owned/Shared (a shared/host-owned store is not disposed).
            await lease.DisposeAsync().ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            // Shared idempotent teardown core; resources dispose LAST in the runtime.
            await DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // The gate may be disposed by a concurrent dispose path; tolerate it.
        }

        _lifecycleCancellation.Cancel();
        _lifecycleCancellation.Dispose();
        _connectCts?.Dispose();

        try
        {
            _gate.Dispose();
        }
        catch (ObjectDisposedException)
        {
        }

        CompleteNode();
    }

    public override void Fault(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        _lifecycleCancellation.Cancel();
        FaultNode(exception);
    }

    private Dictionary<string, object?> CreateStoreAttributes()
        => new(StringComparer.Ordinal)
        {
            ["store"] = StoreName,
            ["nodeType"] = StorageComponentTypes.Store.Value,
            ["address"] = _address.ToString()
        };
}
