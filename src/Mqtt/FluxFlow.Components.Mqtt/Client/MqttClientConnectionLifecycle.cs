using FluxFlow.Components.Mqtt.Configuration;
using FluxFlow.Components.Mqtt.Events;
using FluxFlow.Components.Mqtt.Transport;

namespace FluxFlow.Components.Mqtt.Client;

internal sealed class MqttClientConnectionLifecycle : IAsyncDisposable
{
    private readonly object _stateGate = new();
    private readonly MqttClientConfiguration _configuration;
    private readonly IMqttTransportFactory _transportFactory;
    private readonly TimeProvider _clock;
    private readonly MqttClientSubscriptionState _subscriptions;
    private readonly MqttClientResultFactory _results;
    private readonly MqttClientEventHub _events = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly MqttReceivedMessageDispatcher _receivedMessages;
    private IMqttTransportSession? _session;
    private Task? _messageLoop;
    private Task? _transportEventLoop;
    private Task? _reconnectTask;
    private CancellationTokenSource? _reconnectCancellation;
    private DateTimeOffset? _connectedAt;
    private int _reconnectAttempt;
    private bool _started;
    private bool _reconnectSuppressed;
    private int _disposed;

    internal MqttClientConnectionLifecycle(
        MqttClientConfiguration configuration,
        IMqttTransportFactory transportFactory,
        TimeProvider clock,
        MqttClientSubscriptionState subscriptions,
        MqttClientResultFactory results)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _transportFactory = transportFactory ?? throw new ArgumentNullException(nameof(transportFactory));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _subscriptions = subscriptions ?? throw new ArgumentNullException(nameof(subscriptions));
        _results = results ?? throw new ArgumentNullException(nameof(results));
        _receivedMessages = new MqttReceivedMessageDispatcher(_subscriptions);
    }

    internal string Name => _configuration.Name;

    internal bool IsConnected => Volatile.Read(ref _session)?.IsConnected == true;

    internal bool IsStarted => _started;

    internal bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    internal MqttTransportCapabilities Capabilities =>
        Volatile.Read(ref _session)?.Capabilities ?? new MqttTransportCapabilities();

    internal IMqttTransportSession Session => Volatile.Read(ref _session)
        ?? throw new InvalidOperationException("The MQTT client controller has not started.");

    internal async Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_started)
                return;

            var session = await _transportFactory
                .CreateAsync(_configuration, cancellationToken)
                .ConfigureAwait(false);
            _session = session ?? throw new InvalidOperationException(
                "The MQTT transport factory returned no session.");
            _started = true;
            _messageLoop = RunMessageLoopAsync(session, _lifetime.Token);
            _transportEventLoop = RunTransportEventLoopAsync(session, _lifetime.Token);

            if (_configuration.AutoConnect == MqttAutoConnectMode.OnStart)
                await TryAutoConnectAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    internal ValueTask<IMqttClientEventSubscription> SubscribeEventsAsync(
        int capacity,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        return _events.SubscribeAsync(capacity, cancellationToken);
    }

    internal async ValueTask<T> ExecuteExclusiveAsync<T>(
        Func<IMqttTransportSession, CancellationToken, ValueTask<T>> action,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await action(Session, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    internal async ValueTask ExecuteExclusiveAsync(
        Func<IMqttTransportSession, CancellationToken, ValueTask> action,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await action(Session, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    internal ValueTask PublishEventAsync(
        MqttClientEvent @event,
        CancellationToken cancellationToken)
        => _events.PublishAsync(@event, cancellationToken);

    internal async ValueTask<MqttClientResult> ConnectAsync(CancellationToken cancellationToken)
    {
        CancelReconnect();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _reconnectSuppressed = false;
            try
            {
                var changed = await ConnectCoreAsync(
                    automatic: false,
                    cancellationToken).ConfigureAwait(false);
                return new MqttConnectResult(_clock.GetUtcNow(), changed);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                if (ShouldRetry(exception))
                    ScheduleReconnect();
                return _results.Failure(
                    MqttClientOperation.Connect,
                    MqttClientErrorCodes.ConnectFailed,
                    exception.Message,
                    isTransient: true,
                    exception);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    internal async ValueTask<MqttClientResult> DisconnectAsync(
        string? reason,
        CancellationToken cancellationToken)
    {
        _reconnectSuppressed = true;
        CancelReconnect();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var session = Session;
            if (!session.IsConnected)
                return new MqttDisconnectResult(_clock.GetUtcNow(), changed: false);

            await session.DisconnectAsync(reason, cancellationToken).ConfigureAwait(false);
            await PublishEventAsync(new MqttClientDisconnectedEvent(
                Name,
                _clock.GetUtcNow(),
                expected: true), CancellationToken.None).ConfigureAwait(false);
            return new MqttDisconnectResult(_clock.GetUtcNow(), changed: true);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return _results.Failure(
                MqttClientOperation.Disconnect,
                MqttClientErrorCodes.DisconnectFailed,
                exception.Message,
                isTransient: true,
                exception);
        }
        finally
        {
            _gate.Release();
        }
    }

    internal MqttStatusResult CreateStatusResult(string[] desiredSubscriptions)
    {
        var timestamp = _clock.GetUtcNow();
        return new MqttStatusResult(timestamp, new MqttClientStatus
        {
            Client = Name,
            IsStarted = _started,
            IsConnected = IsConnected,
            ReconnectSuppressed = _reconnectSuppressed,
            DesiredSubscriptions = desiredSubscriptions,
            Timestamp = timestamp
        });
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _lifetime.Cancel();
        CancelReconnect();
        var eventSubscriptions = _events.DetachAll();

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_session is not null)
            {
                if (_session.IsConnected)
                {
                    try
                    {
                        await _session.DisconnectAsync(
                            "Controller disposal",
                            CancellationToken.None).ConfigureAwait(false);
                    }
                    catch
                    {
                    }
                }

                await _session.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }

        await ObserveLoopAsync(_messageLoop).ConfigureAwait(false);
        await ObserveLoopAsync(_transportEventLoop).ConfigureAwait(false);
        await ObserveLoopAsync(_reconnectTask).ConfigureAwait(false);

        foreach (var subscription in eventSubscriptions)
            subscription.Complete();

        _reconnectCancellation?.Dispose();
        _lifetime.Dispose();
        _gate.Dispose();
    }

    private async ValueTask TryAutoConnectAsync(CancellationToken cancellationToken)
    {
        try
        {
            await ConnectCoreAsync(automatic: true, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await PublishEventAsync(new MqttClientDisconnectedEvent(
                Name,
                _clock.GetUtcNow(),
                expected: false,
                MqttClientErrors.Create(
                    MqttClientErrorCodes.ConnectFailed,
                    exception.Message,
                    isTransient: true,
                    exception)), CancellationToken.None).ConfigureAwait(false);
            if (ShouldRetry(exception))
                ScheduleReconnect();
        }
    }

    private async ValueTask<bool> ConnectCoreAsync(
        bool automatic,
        CancellationToken cancellationToken)
    {
        var session = Session;
        if (session.IsConnected)
            return false;

        await session.ConnectAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var subscription in _subscriptions.DesiredSubscriptions())
            {
                await session.SubscribeAsync(
                    subscription.Identity,
                    subscription.Definition,
                    cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception subscriptionFailure)
        {
            try
            {
                await session.DisconnectAsync(
                    "Desired subscription restoration failed.",
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception disconnectFailure)
            {
                throw new AggregateException(subscriptionFailure, disconnectFailure);
            }

            throw;
        }

        var connectedAt = _clock.GetUtcNow();
        lock (_stateGate)
        {
            _connectedAt = connectedAt;
            if (!automatic)
                _reconnectAttempt = 0;
        }
        await PublishEventAsync(new MqttClientConnectedEvent(
            Name,
            connectedAt,
            automatic), CancellationToken.None).ConfigureAwait(false);
        return true;
    }

    private async Task RunMessageLoopAsync(
        IMqttTransportSession session,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var received in session.Messages
                .WithCancellation(cancellationToken)
                .ConfigureAwait(false))
            {
                await _receivedMessages.DispatchAsync(session, received, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task RunTransportEventLoopAsync(
        IMqttTransportSession session,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var transportEvent in session.Events
                .WithCancellation(cancellationToken)
                .ConfigureAwait(false))
            {
                if (transportEvent.Kind != MqttTransportEventKind.Disconnected)
                    continue;

                var expected = _reconnectSuppressed;
                await PublishEventAsync(new MqttClientDisconnectedEvent(
                    Name,
                    _clock.GetUtcNow(),
                    expected,
                    expected
                        ? null
                        : MqttClientErrors.Create(
                            MqttClientErrorCodes.NotConnected,
                            transportEvent.Message ?? "The MQTT transport disconnected.",
                            transportEvent.IsTransient)), CancellationToken.None).ConfigureAwait(false);
                if (!expected)
                    ScheduleReconnect();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void ScheduleReconnect()
    {
        if (!_configuration.Reconnect.Enabled || _reconnectSuppressed || IsDisposed)
            return;

        lock (_stateGate)
        {
            if (_reconnectTask is { IsCompleted: false })
                return;

            _reconnectCancellation?.Dispose();
            if (_connectedAt is { } connectedAt &&
                _clock.GetUtcNow() - connectedAt >= _configuration.Reconnect.Policy.ResetAfter)
            {
                _reconnectAttempt = 0;
            }
            _reconnectCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            _reconnectTask = RunReconnectLoopAsync(_reconnectCancellation.Token);
        }
    }

    private async Task RunReconnectLoopAsync(CancellationToken cancellationToken)
    {
        var policy = _configuration.Reconnect.Policy;
        var started = _clock.GetUtcNow();
        while (!cancellationToken.IsCancellationRequested)
        {
            var attempt = Interlocked.Increment(ref _reconnectAttempt);
            if (_reconnectSuppressed || Session.IsConnected)
                return;
            if (policy.MaximumAttempts is { } maximumAttempts && attempt > maximumAttempts)
                return;
            if (policy.MaximumDuration is { } maximumDuration &&
                _clock.GetUtcNow() - started >= maximumDuration)
            {
                return;
            }

            var delay = policy.GetDelay(attempt);
            await PublishEventAsync(new MqttReconnectScheduledEvent(
                Name,
                _clock.GetUtcNow(),
                attempt,
                delay), CancellationToken.None).ConfigureAwait(false);
            await Task.Delay(delay, _clock, cancellationToken).ConfigureAwait(false);

            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_reconnectSuppressed || Session.IsConnected)
                    return;

                try
                {
                    await ConnectCoreAsync(automatic: true, cancellationToken).ConfigureAwait(false);
                    return;
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    await PublishEventAsync(new MqttClientDisconnectedEvent(
                        Name,
                        _clock.GetUtcNow(),
                        expected: false,
                        MqttClientErrors.Create(
                            MqttClientErrorCodes.ConnectFailed,
                            exception.Message,
                            isTransient: true,
                            exception)), CancellationToken.None).ConfigureAwait(false);
                    if (!ShouldRetry(exception))
                        return;
                }
            }
            finally
            {
                _gate.Release();
            }
        }
    }

    private void CancelReconnect()
    {
        lock (_stateGate)
            _reconnectCancellation?.Cancel();
    }

    private bool ShouldRetry(Exception exception)
    {
        if (exception is not MqttTransportException transportException)
            return true;
        if (!transportException.IsTransient)
            return false;

        var categories = _configuration.Reconnect.Policy.RetryCategories;
        return categories.Count == 0 || categories.Contains(
            transportException.Category,
            StringComparer.OrdinalIgnoreCase);
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(IsDisposed, this);

    private static async Task ObserveLoopAsync(Task? task)
    {
        if (task is null)
            return;
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }
}
