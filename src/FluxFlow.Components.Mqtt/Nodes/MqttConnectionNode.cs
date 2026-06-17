using System.Diagnostics.CodeAnalysis;
using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Components.Mqtt.Diagnostics;
using FluxFlow.Components.Mqtt.Options;
using FluxFlow.Engine.Components;
using FluxFlow.Engine.Definitions;

namespace FluxFlow.Components.Mqtt.Nodes;

/// <summary>
/// Owns the MQTT client lifecycle. Establishing and tearing down the client is an
/// explicit, host-API-only decision: there is no auto-connect, no lazy connect, and
/// no in-graph command port. Publish and subscribe nodes borrow the established
/// adapter via <see cref="TryGetAdapter"/> and never connect or dispose it.
/// </summary>
public sealed class MqttConnectionNode : EventFlowNodeBase, IMqttConnectionHandle, IAsyncDisposable
{
    private readonly NodeAddress _address;
    private readonly IMqttClientFactory _clientFactory;
    private readonly TimeProvider _clock;

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

    internal MqttConnectionNode(
        NodeAddress address,
        string connectionName,
        MqttConnectionProfile profile,
        MqttReconnectPolicy? reconnect,
        IMqttClientFactory clientFactory,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionName);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(clientFactory);
        ArgumentNullException.ThrowIfNull(clock);

        _address = address;
        ConnectionName = connectionName;
        Profile = profile;
        Reconnect = reconnect;
        _clientFactory = clientFactory;
        _clock = clock;
    }

    public string ConnectionName { get; }

    public MqttConnectionProfile Profile { get; }

    public MqttReconnectPolicy? Reconnect { get; }

    public MqttClientHealthState State => _state;

    public int ConnectionEpoch => _epoch;

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
                connect = EstablishAsync(_connectCts.Token);
                _inFlightConnect = connect;
            }
        }
        finally
        {
            _gate.Release();
        }

        await connect.ConfigureAwait(false);
    }

    private async Task<MqttClientLease> EstablishAsync(CancellationToken ct)
    {
        // Yield off the gate-holding caller so the in-flight Task is observable to a
        // concurrent ConnectAsync before CreateAsync runs.
        await Task.Yield();

        MqttClientLease? lease = null;
        try
        {
            var context = new MqttClientFactoryContext
            {
                Address = _address,
                ConnectionName = ConnectionName,
                Profile = Profile,
                Reconnect = Reconnect,
                Clock = _clock
            };

            lease = await _clientFactory.CreateAsync(context, ct).ConfigureAwait(false);
            ArgumentNullException.ThrowIfNull(lease);

            await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                // If a disconnect or dispose won the race while we were creating,
                // honor it: drop the freshly created lease and stay not-connected.
                if (_userDisconnected || _disposed)
                {
                    await lease.DisposeAsync().ConfigureAwait(false);
                    return lease;
                }

                // Publish order: lease FIRST, epoch next, health monitor, then flip
                // state to Connected LAST so a borrow that observes Connected always
                // sees a non-null lease.
                _lease = lease;
                _epoch++;
                _healthMonitor = MqttHealthMonitor.Start(lease.Adapter, _clock, EmitHealth, EmitHealthFailure);
                _state = MqttClientHealthState.Connected;
                _inFlightConnect = null;
                SignalChange();
            }
            finally
            {
                _gate.Release();
            }

            return lease;
        }
        catch (Exception exception)
        {
            // Cancellation here is a requested disconnect/dispose, not a fault: do
            // not clobber the Disconnected state or emit a faulted health event.
            var cancelled = exception is OperationCanceledException &&
                (ct.IsCancellationRequested || _lifecycleCancellation.IsCancellationRequested);

            await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                _inFlightConnect = null;

                // Never leave a half-open lease behind.
                if (lease is not null)
                {
                    await lease.DisposeAsync().ConfigureAwait(false);
                }

                if (!cancelled && !_userDisconnected && !_disposed)
                {
                    _lease = null;
                    _state = MqttClientHealthState.Faulted;
                    SignalChange();
                }
            }
            finally
            {
                _gate.Release();
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

        CompleteNode();
    }

    public override void Fault(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        _lifecycleCancellation.Cancel();
        FaultNode(exception);
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

        TryEmitDiagnostic(
            MqttDiagnosticNames.ConnectionHealthChanged,
            FlowDiagnosticLevel.Error,
            $"MQTT connection '{ConnectionName}' failed to establish a client.",
            exception,
            MqttHealthSignal.CreateDiagnosticAttributes(health, ConnectionName));
        EmitHealthEvent(health);
    }

    private void EmitHealthFailure(MqttClientHealthEvent health, Exception exception)
    {
        TryEmitDiagnostic(
            MqttDiagnosticNames.ConnectionHealthChanged,
            FlowDiagnosticLevel.Error,
            "MQTT connection health stream failed.",
            exception,
            MqttHealthSignal.CreateDiagnosticAttributes(health, ConnectionName));
        EmitHealthEvent(health);
    }

    private void EmitHealth(MqttClientHealthEvent health)
    {
        TryEmitDiagnostic(
            MqttDiagnosticNames.ConnectionHealthChanged,
            MqttHealthSignal.GetLevel(health),
            MqttHealthSignal.CreateMessage(health),
            attributes: MqttHealthSignal.CreateDiagnosticAttributes(health, ConnectionName));
        EmitHealthEvent(health);
    }

    private bool EmitHealthEvent(MqttClientHealthEvent health)
        => EmitEvent(new FlowEvent
        {
            Timestamp = _clock.GetUtcNow(),
            Type = MqttEventNames.ConnectionHealthChanged,
            Source = Id.ToString(),
            SourceNodeId = Id,
            Subject = MqttHealthSignal.CreateSubject(health, ConnectionName),
            Status = health.State.ToString(),
            Channel = MqttEventNames.ConnectionHealthChanged,
            Attributes = MqttHealthSignal.CreateEventAttributes(health, ConnectionName)
        });
}
