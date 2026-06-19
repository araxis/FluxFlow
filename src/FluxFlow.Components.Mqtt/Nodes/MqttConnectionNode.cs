using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Components.Mqtt.Diagnostics;
using FluxFlow.Components.Mqtt.Options;
using FluxFlow.Nodes;

namespace FluxFlow.Components.Mqtt.Nodes;

/// <summary>
/// Owns the MQTT client lifecycle. A connection is a resource, not a dataflow node:
/// it has no input/output data ports, only a self-owned <see cref="Events"/> stream
/// that broadcasts connection-health <see cref="FlowEvent"/>s. Establishing and
/// tearing down the client is an explicit, host-API-only decision: there is no
/// auto-connect, no lazy connect, and no in-graph command port. Publish and subscribe
/// nodes borrow the established adapter via <see cref="TryGetAdapter"/> and never
/// connect or dispose it. Works with nothing but a client factory — no engine.
/// </summary>
public sealed class MqttConnectionNode : IMqttConnectionHandle, IAsyncDisposable
{
    private readonly IMqttClientFactory _clientFactory;
    private readonly TimeProvider _clock;

    // Self-owned event port. Broadcast so connection health can fan out to many
    // observers; a connection handle has no data/error ports.
    private readonly BroadcastBlock<FlowEvent> _events = new(static e => e);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _lifecycleCancellation = new();

    private volatile MqttClientLease? _lease;
    private volatile MqttClientHealthState _state = MqttClientHealthState.Disconnected;
    private volatile int _epoch;

    private Task<MqttClientLease>? _inFlightConnect;
    private CancellationTokenSource? _connectCts;
    private MqttHealthMonitor? _healthMonitor;
    private bool _userDisconnected;
    private bool _disposed;

    // Swapped under the gate; completed on each connect/disconnect transition.
    private TaskCompletionSource _change =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public MqttConnectionNode(
        string connectionName,
        MqttConnectionProfile profile,
        MqttReconnectPolicy? reconnect,
        IMqttClientFactory clientFactory,
        TimeProvider? clock = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionName);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(clientFactory);

        ConnectionName = connectionName;
        Profile = profile;
        Reconnect = reconnect;
        _clientFactory = clientFactory;
        _clock = clock ?? TimeProvider.System;
    }

    public string ConnectionName { get; }

    public MqttConnectionProfile Profile { get; }

    public MqttReconnectPolicy? Reconnect { get; }

    public MqttClientHealthState State => _state;

    public int ConnectionEpoch => _epoch;

    /// <summary>Event port — broadcast; connection-health <see cref="FlowEvent"/> stream.</summary>
    public ISourceBlock<FlowEvent> Events => _events;

    // StartAsync stays a no-op: connecting is an explicit host decision.

    public bool TryGetAdapter([NotNullWhen(true)] out IMqttClientAdapter? adapter)
    {
        // Lock-free borrow. Read state first, then the lease, so a concurrent
        // disconnect (which clears the lease before flipping state) can only ever
        // cause a false negative, never a borrow of a torn-down adapter.
        if (_state == MqttClientHealthState.Connected)
        {
            var lease = _lease;
            if (lease is not null)
            {
                adapter = lease.Adapter;
                return true;
            }
        }

        adapter = null;
        return false;
    }

    public Task WaitForChangeAsync(CancellationToken ct)
        => Volatile.Read(ref _change).Task.WaitAsync(ct);

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        // Idempotent fast path: already connected.
        if (_state == MqttClientHealthState.Connected)
        {
            return;
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        Task<MqttClientLease> connect;
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_state == MqttClientHealthState.Connected)
            {
                return;
            }

            // Single-flight: if a connect is already running, capture it, release
            // the gate, and await it instead of starting a second CreateAsync.
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

                _state = MqttClientHealthState.Connecting;
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

    private async Task<MqttClientLease> EstablishAsync(
        CancellationTokenSource ownCts,
        CancellationToken ct)
    {
        // Yield off the gate-holding caller so the in-flight Task is observable to a
        // concurrent ConnectAsync before CreateAsync runs.
        await Task.Yield();

        MqttClientLease? lease = null;
        try
        {
            var context = new MqttClientFactoryContext
            {
                ConnectionName = ConnectionName,
                Profile = Profile,
                Reconnect = Reconnect,
                Clock = _clock
            };

            lease = await _clientFactory.CreateAsync(context, ct).ConfigureAwait(false);
            ArgumentNullException.ThrowIfNull(lease);

            // Re-acquire the gate to decide-and-publish. DisposeAsync may have disposed
            // the gate while CreateAsync was running, in which case WaitAsync throws
            // ObjectDisposedException; treat that as "cannot publish" and fall through
            // to robust disposal of the freshly built lease so it never leaks.
            var published = false;
            try
            {
                await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
                try
                {
                    // Honor any teardown that won the race while we were creating, drop
                    // a superseded connect, and never overwrite a live lease: a
                    // disconnect/dispose, this establish no longer being the current
                    // in-flight one (a superseding connect replaced ownCts), or an
                    // already-published lease all mean this fresh lease must be dropped,
                    // not published.
                    if (!_disposed &&
                        !_userDisconnected &&
                        ReferenceEquals(_connectCts, ownCts) &&
                        _lease is null)
                    {
                        // Publish order: lease FIRST, epoch next, health monitor, then
                        // flip state to Connected LAST so a borrow that observes
                        // Connected always sees a non-null lease.
                        _lease = lease;
                        _epoch++;
                        _healthMonitor = MqttHealthMonitor.Start(lease.Adapter, _clock, EmitHealth, EmitHealthFailure);
                        _state = MqttClientHealthState.Connected;
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
                // Could not publish (teardown/supersede/already-live lease, or a
                // disposed gate): dispose the fresh lease and return WITHOUT publishing
                // so the live client never leaks.
                await lease.DisposeAsync().ConfigureAwait(false);
                return lease;
            }

            SignalChange();
            return lease;
        }
        catch (Exception exception)
        {
            // Cancellation here is a requested disconnect/dispose, not a fault: do
            // not clobber the Disconnected state or emit a faulted health event.
            var cancelled = exception is OperationCanceledException &&
                (ct.IsCancellationRequested || _lifecycleCancellation.IsCancellationRequested);

            // Mutating shared state needs the gate, but a concurrent DisposeAsync may
            // have disposed it (ObjectDisposedException). Tolerate that and STILL
            // dispose any freshly built lease afterwards so it never leaks.
            var faulted = false;
            try
            {
                await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
                try
                {
                    // Only retract our own in-flight marker; a superseding establish
                    // may already own it. Never clobber a live lease: only fault when
                    // this is still the current connect and nothing was published.
                    if (ReferenceEquals(_connectCts, ownCts))
                    {
                        _inFlightConnect = null;
                    }

                    if (!cancelled && !_userDisconnected && !_disposed &&
                        ReferenceEquals(_connectCts, ownCts) && _lease is null)
                    {
                        _lease = null;
                        _state = MqttClientHealthState.Faulted;
                        faulted = true;
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

            if (faulted)
            {
                SignalChange();
            }

            if (!cancelled && !_userDisconnected && !_disposed)
            {
                EmitFaulted(exception);
            }

            throw;
        }
    }

    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        MqttClientLease? lease;
        MqttHealthMonitor? monitor;
        bool transitioned;
        try
        {
            _userDisconnected = true;
            _connectCts?.Cancel();
            _inFlightConnect = null;

            // Clear the lease FIRST so borrows immediately observe not-connected,
            // and flip state so TryGetAdapter returns false even before teardown.
            lease = Interlocked.Exchange(ref _lease, null);
            monitor = _healthMonitor;
            _healthMonitor = null;

            transitioned = _state != MqttClientHealthState.Disconnected;
            _state = MqttClientHealthState.Disconnected;
        }
        finally
        {
            _gate.Release();
        }

        // Stop+dispose the health monitor BEFORE disposing the lease.
        if (monitor is not null)
        {
            await monitor.DisposeAsync().ConfigureAwait(false);
        }

        if (lease is not null)
        {
            // Idempotent; honors Owned/Shared.
            await lease.DisposeAsync().ConfigureAwait(false);
        }

        if (transitioned)
        {
            SignalChange();
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

        // Complete (flush) the event port rather than fault it, so observers'
        // Completion settles and any buffered health event is still delivered.
        _events.Complete();
    }

    private void SignalChange()
    {
        var previous = Interlocked.Exchange(
            ref _change,
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
        previous.TrySetResult();
    }

    private void EmitFaulted(Exception exception)
    {
        var health = new MqttClientHealthEvent
        {
            Timestamp = _clock.GetUtcNow(),
            State = MqttClientHealthState.Faulted,
            Reason = exception.Message,
            ConnectionName = ConnectionName
        };

        EmitHealthEvent(health, FlowEventLevel.Error);
    }

    private void EmitHealthFailure(MqttClientHealthEvent health, Exception exception)
        => EmitHealthEvent(health, FlowEventLevel.Error);

    private void EmitHealth(MqttClientHealthEvent health)
        => EmitHealthEvent(health, MqttHealthSignal.GetLevel(health));

    private bool EmitHealthEvent(MqttClientHealthEvent health, FlowEventLevel level)
        => _events.Post(new FlowEvent
        {
            Timestamp = _clock.GetUtcNow(),
            Name = MqttEventNames.ConnectionHealthChanged,
            Level = level,
            Message = MqttHealthSignal.CreateMessage(health),
            Attributes = MqttHealthSignal.CreateAttributes(health, ConnectionName)
        });
}
