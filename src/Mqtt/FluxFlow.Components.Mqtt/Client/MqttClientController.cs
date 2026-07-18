using FluxFlow.Components.Mqtt.Acknowledgements;
using FluxFlow.Components.Mqtt.Configuration;
using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Components.Mqtt.Events;
using FluxFlow.Components.Mqtt.Subscriptions;
using FluxFlow.Components.Mqtt.Transport;
using System.Threading.Channels;

namespace FluxFlow.Components.Mqtt.Client;

public sealed class MqttClientController : IMqttClientController
{
    private readonly object _stateGate = new();
    private readonly MqttClientConfiguration _configuration;
    private readonly IMqttTransportFactory _transportFactory;
    private readonly TimeProvider _clock;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Dictionary<string, MqttSubscriptionDefinition> _namedSubscriptions;
    private readonly Dictionary<string, MqttSubscriptionDefinition> _inlineSubscriptions =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, MqttTriggerRegistration> _triggers =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _claims = new(StringComparer.Ordinal);
    private readonly List<MqttClientEventSubscription> _eventSubscriptions = [];
    private IMqttTransportSession? _session;
    private Task? _messageLoop;
    private Task? _transportEventLoop;
    private Task? _reconnectTask;
    private CancellationTokenSource? _reconnectCancellation;
    private MqttClientEvent? _lastConnectionEvent;
    private DateTimeOffset? _connectedAt;
    private int _reconnectAttempt;
    private bool _started;
    private bool _reconnectSuppressed;
    private int _disposed;

    public MqttClientController(
        MqttClientConfiguration configuration,
        IMqttTransportFactory transportFactory,
        TimeProvider? clock = null)
    {
        _configuration = ValidateConfiguration(configuration);
        _transportFactory = transportFactory ?? throw new ArgumentNullException(nameof(transportFactory));
        _clock = clock ?? TimeProvider.System;
        _namedSubscriptions = new Dictionary<string, MqttSubscriptionDefinition>(
            configuration.Subscriptions,
            StringComparer.Ordinal);
    }

    public string Name => _configuration.Name;

    public bool IsConnected => Volatile.Read(ref _session)?.IsConnected == true;

    public MqttTransportCapabilities Capabilities =>
        Volatile.Read(ref _session)?.Capabilities ?? new MqttTransportCapabilities();

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
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
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async ValueTask<MqttClientResult> ExecuteAsync(
        MqttClientRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ThrowIfDisposed();

        if (!_started)
        {
            return Failure(
                request.Operation,
                MqttClientErrorCodes.NotStarted,
                "The MQTT client controller has not started.",
                isTransient: false);
        }

        try
        {
            return request switch
            {
                MqttConnectRequest => await ConnectAsync(cancellationToken).ConfigureAwait(false),
                MqttDisconnectRequest disconnect => await DisconnectAsync(
                    disconnect.Reason,
                    cancellationToken).ConfigureAwait(false),
                MqttStatusRequest => CreateStatusResult(),
                MqttPublishClientRequest publish => await PublishAsync(
                    publish.Message,
                    cancellationToken).ConfigureAwait(false),
                MqttSubscribeRequest subscribe => await SubscribeAsync(
                    subscribe,
                    cancellationToken).ConfigureAwait(false),
                MqttUnsubscribeRequest unsubscribe => await UnsubscribeAsync(
                    unsubscribe,
                    cancellationToken).ConfigureAwait(false),
                _ => Failure(
                    request.Operation,
                    MqttClientErrorCodes.InvalidRequest,
                    $"Unsupported MQTT client request '{request.GetType().Name}'.",
                    isTransient: false)
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ArgumentException exception)
        {
            return Failure(
                request.Operation,
                MqttClientErrorCodes.InvalidRequest,
                exception.Message,
                isTransient: false,
                exception);
        }
        catch (MqttTransportException exception)
        {
            return Failure(
                request.Operation,
                ErrorCodeFor(request.Operation),
                exception.Message,
                exception.IsTransient,
                exception);
        }
        catch (Exception exception)
        {
            return Failure(
                request.Operation,
                ErrorCodeFor(request.Operation),
                exception.Message,
                isTransient: true,
                exception);
        }
    }

    public async ValueTask<IMqttTriggerRegistration> RegisterTriggerAsync(
        MqttTriggerRegistrationOptions options,
        CancellationToken cancellationToken = default)
    {
        ValidateTriggerOptions(options);
        ThrowIfDisposed();
        if (!_started)
            throw new InvalidOperationException("The MQTT client controller has not started.");

        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var registration = new MqttTriggerRegistration(options, RemoveTriggerAsync);
            var inlineToSubscribe = new List<(string Identity, MqttSubscriptionDefinition Definition)>();

            lock (_stateGate)
            {
                if (_triggers.ContainsKey(options.TriggerId))
                {
                    throw new InvalidOperationException(
                        $"MQTT trigger '{options.TriggerId}' is already registered for client '{Name}'.");
                }

                foreach (var target in options.Subscriptions)
                {
                    if (_claims.TryGetValue(target.Identity, out var owner) &&
                        !string.Equals(owner, options.TriggerId, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"MQTT subscription '{target.Identity}' is already claimed by trigger '{owner}'.");
                    }

                    var definition = ResolveDefinition(target);
                    if (definition is not null &&
                        FindFilterOwner(definition.TopicFilter) is { } filterOwner &&
                        !string.Equals(filterOwner, options.TriggerId, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"MQTT topic filter '{definition.TopicFilter}' is already claimed by trigger '{filterOwner}'.");
                    }
                }

                _triggers.Add(options.TriggerId, registration);
                foreach (var target in options.Subscriptions)
                {
                    _claims[target.Identity] = options.TriggerId;
                    if (target.Inline is not null)
                    {
                        _inlineSubscriptions[target.Identity] = target.Inline;
                        inlineToSubscribe.Add((target.Identity, target.Inline));
                    }
                }
            }

            var subscribed = new List<string>();
            try
            {
                if (_session!.IsConnected)
                {
                    foreach (var inline in inlineToSubscribe)
                    {
                        await _session.SubscribeAsync(
                            inline.Identity,
                            inline.Definition,
                            cancellationToken).ConfigureAwait(false);
                        subscribed.Add(inline.Identity);
                    }
                }

                return registration;
            }
            catch
            {
                foreach (var identity in subscribed)
                {
                    try
                    {
                        await _session!.UnsubscribeAsync(identity, CancellationToken.None)
                            .ConfigureAwait(false);
                    }
                    catch
                    {
                    }
                }
                await RemoveTriggerCoreAsync(registration, unsubscribe: false).ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public ValueTask<IMqttClientEventSubscription> SubscribeEventsAsync(
        int capacity = 128,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));

        var subscription = new MqttClientEventSubscription(capacity, RemoveEventSubscription);
        lock (_stateGate)
        {
            _eventSubscriptions.Add(subscription);
            if (_lastConnectionEvent is not null)
                subscription.TryWrite(_lastConnectionEvent);
        }
        return ValueTask.FromResult<IMqttClientEventSubscription>(subscription);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _lifetime.Cancel();
        CancelReconnect();

        MqttTriggerRegistration[] triggers;
        MqttClientEventSubscription[] eventSubscriptions;
        lock (_stateGate)
        {
            triggers = _triggers.Values.ToArray();
            eventSubscriptions = _eventSubscriptions.ToArray();
            _triggers.Clear();
            _claims.Clear();
            _inlineSubscriptions.Clear();
            _eventSubscriptions.Clear();
        }

        foreach (var trigger in triggers)
            trigger.Complete();

        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
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
            _lifecycleGate.Release();
        }

        await ObserveLoopAsync(_messageLoop).ConfigureAwait(false);
        await ObserveLoopAsync(_transportEventLoop).ConfigureAwait(false);
        await ObserveLoopAsync(_reconnectTask).ConfigureAwait(false);

        foreach (var subscription in eventSubscriptions)
            subscription.Complete();

        _reconnectCancellation?.Dispose();
        _lifetime.Dispose();
        _lifecycleGate.Dispose();
    }

    private async ValueTask<MqttClientResult> ConnectAsync(CancellationToken cancellationToken)
    {
        CancelReconnect();
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
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
                return Failure(
                    MqttClientOperation.Connect,
                    MqttClientErrorCodes.ConnectFailed,
                    exception.Message,
                    isTransient: true,
                    exception);
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private async ValueTask<MqttClientResult> DisconnectAsync(
        string? reason,
        CancellationToken cancellationToken)
    {
        _reconnectSuppressed = true;
        CancelReconnect();
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var session = _session!;
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
            return Failure(
                MqttClientOperation.Disconnect,
                MqttClientErrorCodes.DisconnectFailed,
                exception.Message,
                isTransient: true,
                exception);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private async ValueTask<MqttClientResult> PublishAsync(
        MqttPublishMessage message,
        CancellationToken cancellationToken)
    {
        ValidatePublishMessage(message);
        if (!_session!.IsConnected)
        {
            return Failure(
                MqttClientOperation.Publish,
                MqttClientErrorCodes.NotConnected,
                "The MQTT client is not connected.",
                isTransient: true);
        }

        await _session.PublishAsync(message, cancellationToken).ConfigureAwait(false);
        return new MqttPublishOperationResult(_clock.GetUtcNow(), message);
    }

    private async ValueTask<MqttClientResult> SubscribeAsync(
        MqttSubscribeRequest request,
        CancellationToken cancellationToken)
    {
        ValidateName(request.Name, nameof(request.Name));
        ValidateSubscription(request.Subscription);
        if (!_session!.IsConnected)
        {
            return Failure(
                MqttClientOperation.Subscribe,
                MqttClientErrorCodes.NotConnected,
                "The MQTT client is not connected.",
                isTransient: true);
        }

        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var name = request.Name.Trim();
            lock (_stateGate)
            {
                if (_namedSubscriptions.TryGetValue(name, out var existing) &&
                    existing == request.Subscription)
                {
                    return new MqttSubscribeResult(
                        _clock.GetUtcNow(),
                        name,
                        existing,
                        changed: false);
                }

                var claimedBy = _triggers.Values
                    .Where(registration => registration.Options.Subscriptions.Any(
                        target => string.Equals(target.Name, name, StringComparison.Ordinal)))
                    .Select(registration => registration.Options.TriggerId)
                    .SingleOrDefault();
                if (claimedBy is not null &&
                    FindFilterOwner(request.Subscription.TopicFilter) is { } filterOwner &&
                    !string.Equals(filterOwner, claimedBy, StringComparison.Ordinal))
                {
                    return Failure(
                        MqttClientOperation.Subscribe,
                        MqttClientErrorCodes.InvalidRequest,
                        $"MQTT topic filter '{request.Subscription.TopicFilter}' is already claimed by trigger '{filterOwner}'.",
                        isTransient: false);
                }
            }

            await _session.SubscribeAsync(
                $"name:{name}",
                request.Subscription,
                cancellationToken).ConfigureAwait(false);
            lock (_stateGate)
                _namedSubscriptions[name] = request.Subscription;
            await PublishEventAsync(new MqttSubscriptionChangedEvent(
                Name,
                _clock.GetUtcNow(),
                name,
                subscribed: true), CancellationToken.None).ConfigureAwait(false);
            return new MqttSubscribeResult(
                _clock.GetUtcNow(),
                name,
                request.Subscription,
                changed: true);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private async ValueTask<MqttClientResult> UnsubscribeAsync(
        MqttUnsubscribeRequest request,
        CancellationToken cancellationToken)
    {
        ValidateName(request.Name, nameof(request.Name));
        if (!_session!.IsConnected)
        {
            return Failure(
                MqttClientOperation.Unsubscribe,
                MqttClientErrorCodes.NotConnected,
                "The MQTT client is not connected.",
                isTransient: true);
        }

        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var name = request.Name.Trim();
            lock (_stateGate)
            {
                if (!_namedSubscriptions.ContainsKey(name))
                    return new MqttUnsubscribeResult(_clock.GetUtcNow(), name, changed: false);
            }

            await _session.UnsubscribeAsync($"name:{name}", cancellationToken).ConfigureAwait(false);
            lock (_stateGate)
                _namedSubscriptions.Remove(name);
            await PublishEventAsync(new MqttSubscriptionChangedEvent(
                Name,
                _clock.GetUtcNow(),
                name,
                subscribed: false), CancellationToken.None).ConfigureAwait(false);
            return new MqttUnsubscribeResult(_clock.GetUtcNow(), name, changed: true);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private MqttStatusResult CreateStatusResult()
    {
        string[] desired;
        lock (_stateGate)
        {
            desired = _namedSubscriptions.Keys
                .Select(static name => $"name:{name}")
                .Concat(_inlineSubscriptions.Keys)
                .Order(StringComparer.Ordinal)
                .ToArray();
        }

        var timestamp = _clock.GetUtcNow();
        return new MqttStatusResult(timestamp, new MqttClientStatus
        {
            Client = Name,
            IsStarted = _started,
            IsConnected = IsConnected,
            ReconnectSuppressed = _reconnectSuppressed,
            DesiredSubscriptions = desired,
            Timestamp = timestamp
        });
    }

    private async ValueTask<bool> ConnectCoreAsync(
        bool automatic,
        CancellationToken cancellationToken)
    {
        var session = _session!;
        if (session.IsConnected)
            return false;

        await session.ConnectAsync(cancellationToken).ConfigureAwait(false);
        (string Identity, MqttSubscriptionDefinition Definition)[] desired;
        lock (_stateGate)
        {
            desired = _namedSubscriptions
                .Select(static item => ($"name:{item.Key}", item.Value))
                .Concat(_inlineSubscriptions.Select(static item => (item.Key, item.Value)))
                .OrderBy(static item => item.Item1, StringComparer.Ordinal)
                .ToArray();
        }

        try
        {
            foreach (var subscription in desired)
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
                await DispatchReceivedAsync(received, cancellationToken).ConfigureAwait(false);
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

    private async Task DispatchReceivedAsync(
        MqttTransportReceivedMessage received,
        CancellationToken cancellationToken)
    {
        (MqttTriggerRegistration Registration, string[] Matches)[] targets;
        lock (_stateGate)
        {
            targets = _triggers.Values
                .Select(registration => (
                    Registration: registration,
                    Matches: ResolveMatches(registration.Options, received.Message.Topic)))
                .Where(static item => item.Matches.Length > 0)
                .ToArray();
        }

        if (received.Message.Qos == MqttQos.AtMostOnce || received.Delivery.IsEmpty)
        {
            await Task.WhenAll(targets.Select(target => DispatchToTriggerAsync(
                target.Registration,
                target.Matches,
                received,
                acknowledgement: null,
                cancellationToken))).ConfigureAwait(false);
            return;
        }

        if (targets.Length == 0)
        {
            await _session!.AcknowledgeAsync(
                received.Delivery,
                MqttWorkflowOutcome.Ack,
                cancellationToken).ConfigureAwait(false);
            return;
        }

        var acknowledgement = new BrokerAcknowledgementCoordinator(
            _session!,
            received.Delivery,
            targets.Length);
        await Task.WhenAll(targets.Select(target => DispatchToTriggerAsync(
            target.Registration,
            target.Matches,
            received,
            acknowledgement,
            cancellationToken))).ConfigureAwait(false);
    }

    private async Task DispatchToTriggerAsync(
        MqttTriggerRegistration registration,
        string[] matches,
        MqttTransportReceivedMessage received,
        BrokerAcknowledgementCoordinator? acknowledgement,
        CancellationToken cancellationToken)
    {
        MqttTriggerDelivery? delivery = null;
        try
        {
            var message = received.Message with { MatchedSubscriptions = matches };
            delivery = new MqttTriggerDelivery(
                message,
                acknowledgement is null
                    ? static (_, _) => ValueTask.CompletedTask
                    : acknowledgement.CompleteAsync);
            await registration.WriteAsync(delivery, cancellationToken).ConfigureAwait(false);

            if (registration.Options.BrokerAcknowledgement ==
                MqttBrokerAcknowledgement.Automatic)
            {
                await delivery.CompleteBrokerAcknowledgementAsync(
                    MqttWorkflowOutcome.Ack,
                    cancellationToken).ConfigureAwait(false);
            }
        }
        catch (ChannelClosedException)
        {
            // A trigger revision can close after the dispatch snapshot was captured.
            if (delivery is not null)
            {
                await delivery.CompleteBrokerAcknowledgementAsync(
                    MqttWorkflowOutcome.Nak,
                    CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    private string[] ResolveMatches(MqttTriggerRegistrationOptions options, string topic)
        => options.Subscriptions
            .Select(target => (
                Target: target,
                Definition: target.Inline ??
                    (target.Name is not null && _namedSubscriptions.TryGetValue(target.Name, out var named)
                        ? named
                        : null)))
            .Where(item => item.Definition is not null &&
                MqttTopicFilterMatcher.IsMatch(topic, item.Definition.TopicFilter))
            .Select(item => item.Target.Name ?? item.Definition!.TopicFilter)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private async ValueTask RemoveTriggerAsync(MqttTriggerRegistration registration)
    {
        if (_disposed != 0)
            return;

        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await RemoveTriggerCoreAsync(registration, unsubscribe: true).ConfigureAwait(false);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private async ValueTask RemoveTriggerCoreAsync(
        MqttTriggerRegistration registration,
        bool unsubscribe)
    {
        List<string> inlineIdentities = [];
        lock (_stateGate)
        {
            if (!_triggers.Remove(registration.Options.TriggerId))
                return;

            foreach (var target in registration.Options.Subscriptions)
            {
                _claims.Remove(target.Identity);
                if (target.Inline is not null)
                {
                    _inlineSubscriptions.Remove(target.Identity);
                    inlineIdentities.Add(target.Identity);
                }
            }
        }

        if (unsubscribe && _session?.IsConnected == true)
        {
            List<Exception>? failures = null;
            foreach (var identity in inlineIdentities)
            {
                try
                {
                    await _session.UnsubscribeAsync(identity, CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    (failures ??= []).Add(exception);
                }
            }

            if (failures is not null)
                throw new AggregateException("One or more MQTT inline subscriptions could not be removed.", failures);
        }
    }

    private async ValueTask PublishEventAsync(
        MqttClientEvent @event,
        CancellationToken cancellationToken)
    {
        MqttClientEventSubscription[] subscriptions;
        lock (_stateGate)
        {
            if (@event is MqttClientConnectedEvent or MqttClientDisconnectedEvent)
            {
                if ((_lastConnectionEvent is MqttClientConnectedEvent &&
                        @event is MqttClientConnectedEvent) ||
                    (_lastConnectionEvent is MqttClientDisconnectedEvent &&
                        @event is MqttClientDisconnectedEvent))
                {
                    return;
                }

                _lastConnectionEvent = @event;
            }
            subscriptions = _eventSubscriptions.ToArray();
        }

        foreach (var subscription in subscriptions)
        {
            try
            {
                await subscription.WriteAsync(@event, cancellationToken).ConfigureAwait(false);
            }
            catch (ChannelClosedException)
            {
                // A subscriber can close after the publication snapshot was captured.
            }
        }
    }

    private void RemoveEventSubscription(MqttClientEventSubscription subscription)
    {
        lock (_stateGate)
            _eventSubscriptions.Remove(subscription);
    }

    private void ScheduleReconnect()
    {
        if (!_configuration.Reconnect.Enabled || _reconnectSuppressed || _disposed != 0)
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
            if (_reconnectSuppressed || _session?.IsConnected == true)
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

            await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_reconnectSuppressed || _session!.IsConnected)
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
                _lifecycleGate.Release();
            }
        }
    }

    private void CancelReconnect()
    {
        lock (_stateGate)
            _reconnectCancellation?.Cancel();
    }

    private MqttClientFailureResult Failure(
        MqttClientOperation operation,
        string code,
        string message,
        bool isTransient,
        Exception? exception = null)
        => new(
            operation,
            MqttClientErrors.Create(code, message, isTransient, exception),
            _clock.GetUtcNow());

    private static string ErrorCodeFor(MqttClientOperation operation)
        => operation switch
        {
            MqttClientOperation.Connect => MqttClientErrorCodes.ConnectFailed,
            MqttClientOperation.Disconnect => MqttClientErrorCodes.DisconnectFailed,
            MqttClientOperation.Publish => MqttClientErrorCodes.PublishFailed,
            MqttClientOperation.Subscribe => MqttClientErrorCodes.SubscribeFailed,
            MqttClientOperation.Unsubscribe => MqttClientErrorCodes.UnsubscribeFailed,
            _ => MqttClientErrorCodes.InvalidRequest
        };

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

    private void ValidateTriggerOptions(MqttTriggerRegistrationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidateName(options.TriggerId, nameof(options.TriggerId));
        if (options.Subscriptions.Count == 0)
            throw new ArgumentException("An MQTT trigger requires at least one subscription.", nameof(options));
        if (options.MaximumPendingMessages <= 0)
            throw new ArgumentOutOfRangeException(nameof(options.MaximumPendingMessages));
        if (!Enum.IsDefined(options.WorkflowAcknowledgement))
            throw new ArgumentOutOfRangeException(nameof(options.WorkflowAcknowledgement));
        if (!Enum.IsDefined(options.BrokerAcknowledgement))
            throw new ArgumentOutOfRangeException(nameof(options.BrokerAcknowledgement));
        if (options.BrokerAcknowledgement == MqttBrokerAcknowledgement.AfterOutcome &&
            options.WorkflowAcknowledgement != MqttWorkflowAcknowledgement.Required)
        {
            throw new ArgumentException(
                "Broker acknowledgement after outcome requires workflow acknowledgement.",
                nameof(options));
        }

        var canReceiveAcknowledgedDelivery = options.Subscriptions.Any(target =>
        {
            var definition = ResolveDefinition(target);
            return definition is null || definition.Qos != MqttQos.AtMostOnce;
        });
        var capabilities = Capabilities;
        if (canReceiveAcknowledgedDelivery &&
            options.BrokerAcknowledgement != MqttBrokerAcknowledgement.Automatic &&
            !capabilities.DeferredAcknowledgement)
        {
            throw new NotSupportedException(
                "The MQTT transport does not support deferred broker acknowledgement.");
        }
        if (canReceiveAcknowledgedDelivery &&
            options.BrokerAcknowledgement == MqttBrokerAcknowledgement.AfterOutcome &&
            !capabilities.NegativeAcknowledgement)
        {
            throw new NotSupportedException(
                "The MQTT transport does not support negative broker acknowledgement.");
        }

        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var target in options.Subscriptions)
        {
            if (!identities.Add(target.Identity))
                throw new ArgumentException(
                    $"MQTT trigger subscription '{target.Identity}' is duplicated.",
                    nameof(options));
            if (target.Inline is not null)
                ValidateSubscription(target.Inline);
        }
    }

    private static MqttClientConfiguration ValidateConfiguration(
        MqttClientConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ValidateName(configuration.Name, nameof(configuration.Name));
        ArgumentException.ThrowIfNullOrWhiteSpace(
            configuration.ClientId,
            nameof(configuration.ClientId));
        ArgumentNullException.ThrowIfNull(configuration.Broker);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            configuration.Broker.Host,
            nameof(configuration.Broker.Host));
        if (configuration.Broker.Port is <= 0 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(configuration.Broker.Port));
        if (configuration.KeepAlive <= TimeSpan.Zero ||
            configuration.KeepAlive > TimeSpan.FromSeconds(ushort.MaxValue))
            throw new ArgumentOutOfRangeException(nameof(configuration.KeepAlive));
        if (!Enum.IsDefined(configuration.AutoConnect))
            throw new ArgumentOutOfRangeException(nameof(configuration.AutoConnect));
        ValidateRetryPolicy(configuration.Reconnect.Policy);
        if (configuration.LastWill is not null)
            ValidatePublishMessage(configuration.LastWill);
        foreach (var certificate in configuration.Certificates)
        {
            ArgumentNullException.ThrowIfNull(certificate);
            ArgumentException.ThrowIfNullOrWhiteSpace(certificate.Name);
            if (certificate.Content.IsEmpty)
                throw new ArgumentException(
                    $"MQTT client certificate '{certificate.Name}' has no content.",
                    nameof(configuration.Certificates));
        }
        foreach (var subscription in configuration.Subscriptions)
        {
            ValidateName(subscription.Key, nameof(configuration.Subscriptions));
            ValidateSubscription(subscription.Value);
        }

        return configuration;
    }

    private static void ValidateRetryPolicy(MqttRetryPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        if (!Enum.IsDefined(policy.Strategy))
            throw new ArgumentOutOfRangeException(nameof(policy.Strategy));
        if (policy.InitialDelay < TimeSpan.Zero ||
            policy.Increment < TimeSpan.Zero ||
            policy.MaximumDelay < TimeSpan.Zero ||
            policy.ResetAfter < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(policy));
        }
        if (policy.MaximumAttempts <= 0)
            throw new ArgumentOutOfRangeException(nameof(policy.MaximumAttempts));
        if (policy.MaximumDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(policy.MaximumDuration));
        if (policy.JitterFactor is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(policy.JitterFactor));
    }

    private static void ValidateSubscription(MqttSubscriptionDefinition subscription)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        ArgumentException.ThrowIfNullOrWhiteSpace(subscription.TopicFilter);
        var validation = Validation.MqttTopicValidator.ValidateSubscriptionFilter(
            subscription.TopicFilter);
        if (!validation.IsValid)
            throw new ArgumentException(validation.Message, nameof(subscription));
        if (!Enum.IsDefined(subscription.Qos))
            throw new ArgumentOutOfRangeException(nameof(subscription.Qos));
        if (!Enum.IsDefined(subscription.RetainHandling))
            throw new ArgumentOutOfRangeException(nameof(subscription.RetainHandling));
    }

    private static void ValidateName(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
    }

    private static void ValidatePublishMessage(MqttPublishMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var validation = Validation.MqttTopicValidator.ValidatePublishTopic(message.Topic);
        if (!validation.IsValid)
            throw new ArgumentException(validation.Message, nameof(message));
        ArgumentNullException.ThrowIfNull(message.Content);
        if (!message.Content.HasOriginalRepresentation)
        {
            throw new ArgumentException(
                "An MQTT publish message requires FlowContent with an exact byte representation.",
                nameof(message));
        }
        if (!Enum.IsDefined(message.Qos))
            throw new ArgumentOutOfRangeException(nameof(message.Qos));
        if (!string.IsNullOrWhiteSpace(message.ResponseTopic))
        {
            var responseValidation = Validation.MqttTopicValidator.ValidatePublishTopic(
                message.ResponseTopic);
            if (!responseValidation.IsValid)
                throw new ArgumentException(responseValidation.Message, nameof(message));
        }
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(_disposed != 0, this);

    private MqttSubscriptionDefinition? ResolveDefinition(MqttSubscriptionTarget target)
        => target.Inline ??
            (target.Name is not null && _namedSubscriptions.TryGetValue(target.Name, out var named)
                ? named
                : null);

    private string? FindFilterOwner(string topicFilter)
    {
        foreach (var registration in _triggers.Values)
        {
            foreach (var target in registration.Options.Subscriptions)
            {
                var definition = ResolveDefinition(target);
                if (definition is not null &&
                    string.Equals(definition.TopicFilter, topicFilter, StringComparison.Ordinal))
                {
                    return registration.Options.TriggerId;
                }
            }
        }

        return null;
    }

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

    private sealed class BrokerAcknowledgementCoordinator(
        IMqttTransportSession session,
        MqttTransportDeliveryToken delivery,
        int participants)
    {
        private readonly object _gate = new();
        private int _remaining = participants;
        private MqttWorkflowOutcome _outcome = MqttWorkflowOutcome.Ack;

        internal ValueTask CompleteAsync(
            MqttWorkflowOutcome outcome,
            CancellationToken cancellationToken)
        {
            MqttWorkflowOutcome finalOutcome;
            lock (_gate)
            {
                if (_remaining <= 0)
                    return ValueTask.CompletedTask;

                if (Priority(outcome) > Priority(_outcome))
                    _outcome = outcome;
                _remaining--;
                if (_remaining != 0)
                    return ValueTask.CompletedTask;
                finalOutcome = _outcome;
            }

            return session.AcknowledgeAsync(delivery, finalOutcome, cancellationToken);
        }

        private static int Priority(MqttWorkflowOutcome outcome)
            => outcome switch
            {
                MqttWorkflowOutcome.Ack => 0,
                MqttWorkflowOutcome.Timeout => 1,
                MqttWorkflowOutcome.Nak => 2,
                _ => throw new ArgumentOutOfRangeException(nameof(outcome))
            };
    }
}
