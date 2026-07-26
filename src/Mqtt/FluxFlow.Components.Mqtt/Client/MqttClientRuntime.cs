using FluxFlow.Components.Mqtt.Configuration;
using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Components.Mqtt.Events;
using FluxFlow.Components.Mqtt.Subscriptions;
using FluxFlow.Components.Mqtt.Transport;
using FluxFlow.Resilience;

namespace FluxFlow.Components.Mqtt.Client;

internal sealed class MqttClientRuntime : IMqttClientCommandOperations
{
    private readonly TimeProvider _clock;
    private readonly MqttClientResultFactory _results;
    private readonly MqttClientCommandDispatcher _commands;
    private readonly MqttClientSubscriptionState _subscriptions;
    private readonly MqttClientConnectionLifecycle _connection;
    private int _disposed;

    internal MqttClientRuntime(
        MqttClientConfiguration configuration,
        IMqttTransportFactory transportFactory,
        TimeProvider? clock = null,
        IRetryJitterSource? jitterSource = null)
    {
        var validated = MqttClientConfigurationValidator.Validate(configuration);
        _clock = clock ?? TimeProvider.System;
        _results = new MqttClientResultFactory(_clock);
        _subscriptions = new MqttClientSubscriptionState(validated.Subscriptions);
        _connection = new MqttClientConnectionLifecycle(
            validated,
            transportFactory,
            _clock,
            _subscriptions,
            _results,
            jitterSource ?? RandomRetryJitterSource.Shared);
        _commands = new MqttClientCommandDispatcher(this, _results);
    }

    public string Name => _connection.Name;

    public bool IsConnected => _connection.IsConnected;

    public MqttTransportCapabilities Capabilities => _connection.Capabilities;

    public bool IsStarted => _connection.IsStarted;

    public Task StartAsync(CancellationToken cancellationToken = default)
        => _connection.StartAsync(cancellationToken);

    public ValueTask<MqttClientResult> ExecuteAsync(
        MqttClientRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ThrowIfDisposed();
        return _commands.ExecuteAsync(request, cancellationToken);
    }

    public async ValueTask<IMqttTriggerRegistration> RegisterTriggerAsync(
        MqttTriggerRegistrationOptions options,
        CancellationToken cancellationToken = default)
    {
        MqttClientConfigurationValidator.ValidateTriggerOptions(
            options,
            _subscriptions.Resolve,
            Capabilities);
        ThrowIfDisposed();
        if (!IsStarted)
            throw new InvalidOperationException("The MQTT client controller has not started.");

        return await _connection.ExecuteExclusiveAsync<MqttTriggerRegistration>(
            async (session, token) =>
            {
                var inlineToSubscribe =
                    new List<(string Identity, MqttSubscriptionDefinition Definition)>();
                var registration = _subscriptions.AddTrigger(
                    Name,
                    options,
                    RemoveTriggerAsync,
                    inlineToSubscribe);

                var subscribed = new List<string>();
                try
                {
                    if (session.IsConnected)
                    {
                        foreach (var inline in inlineToSubscribe)
                        {
                            await session.SubscribeAsync(
                                inline.Identity,
                                inline.Definition,
                                token).ConfigureAwait(false);
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
                            await session.UnsubscribeAsync(identity, CancellationToken.None)
                                .ConfigureAwait(false);
                        }
                        catch
                        {
                        }
                    }

                    await RemoveTriggerCoreAsync(registration, unsubscribe: false, session)
                        .ConfigureAwait(false);
                    throw;
                }
            },
            cancellationToken).ConfigureAwait(false);
    }

    public ValueTask<IMqttClientEventSubscription> SubscribeEventsAsync(
        int capacity = 128,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return _connection.SubscribeEventsAsync(capacity, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        var triggers = _subscriptions.DetachAllTriggers();
        foreach (var trigger in triggers)
            trigger.Complete();

        await _connection.DisposeAsync().ConfigureAwait(false);
    }

    public ValueTask<MqttClientResult> ConnectAsync(CancellationToken cancellationToken)
        => _connection.ConnectAsync(cancellationToken);

    public ValueTask<MqttClientResult> DisconnectAsync(
        string? reason,
        CancellationToken cancellationToken)
        => _connection.DisconnectAsync(reason, cancellationToken);

    public MqttStatusResult CreateStatusResult()
        => _connection.CreateStatusResult(_subscriptions.DesiredIdentities());

    public async ValueTask<MqttClientResult> PublishAsync(
        MqttPublishMessage message,
        CancellationToken cancellationToken)
    {
        MqttClientConfigurationValidator.ValidatePublishMessage(message);
        var session = _connection.Session;
        if (!session.IsConnected)
        {
            return _results.Failure(
                MqttClientOperation.Publish,
                MqttClientErrorCodes.NotConnected,
                "The MQTT client is not connected.",
                isTransient: true);
        }

        await session.PublishAsync(message, cancellationToken).ConfigureAwait(false);
        return new MqttPublishOperationResult(_clock.GetUtcNow(), message);
    }

    public async ValueTask<MqttClientResult> SubscribeAsync(
        MqttSubscribeRequest request,
        CancellationToken cancellationToken)
    {
        MqttClientConfigurationValidator.ValidateName(request.Name, nameof(request.Name));
        MqttClientConfigurationValidator.ValidateSubscription(request.Subscription);
        if (!_connection.Session.IsConnected)
        {
            return _results.Failure(
                MqttClientOperation.Subscribe,
                MqttClientErrorCodes.NotConnected,
                "The MQTT client is not connected.",
                isTransient: true);
        }

        return await _connection.ExecuteExclusiveAsync<MqttClientResult>(
            async (session, token) =>
            {
                var name = request.Name.Trim();
                var decision = _subscriptions.EvaluateNamedChange(name, request.Subscription);
                if (decision.Existing is not null)
                {
                    return new MqttSubscribeResult(
                        _clock.GetUtcNow(),
                        name,
                        decision.Existing,
                        changed: false);
                }
                if (decision.ConflictOwner is not null)
                {
                    return _results.Failure(
                        MqttClientOperation.Subscribe,
                        MqttClientErrorCodes.InvalidRequest,
                        $"MQTT topic filter '{request.Subscription.TopicFilter}' is already claimed by trigger '{decision.ConflictOwner}'.",
                        isTransient: false);
                }

                await session.SubscribeAsync(
                    $"name:{name}",
                    request.Subscription,
                    token).ConfigureAwait(false);
                _subscriptions.SetNamed(name, request.Subscription);
                await _connection.PublishEventAsync(new MqttSubscriptionChangedEvent(
                    Name,
                    _clock.GetUtcNow(),
                    name,
                    subscribed: true), CancellationToken.None).ConfigureAwait(false);
                return new MqttSubscribeResult(
                    _clock.GetUtcNow(),
                    name,
                    request.Subscription,
                    changed: true);
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<MqttClientResult> UnsubscribeAsync(
        MqttUnsubscribeRequest request,
        CancellationToken cancellationToken)
    {
        MqttClientConfigurationValidator.ValidateName(request.Name, nameof(request.Name));
        if (!_connection.Session.IsConnected)
        {
            return _results.Failure(
                MqttClientOperation.Unsubscribe,
                MqttClientErrorCodes.NotConnected,
                "The MQTT client is not connected.",
                isTransient: true);
        }

        return await _connection.ExecuteExclusiveAsync<MqttClientResult>(
            async (session, token) =>
            {
                var name = request.Name.Trim();
                if (!_subscriptions.ContainsNamed(name))
                    return new MqttUnsubscribeResult(_clock.GetUtcNow(), name, changed: false);

                await session.UnsubscribeAsync($"name:{name}", token).ConfigureAwait(false);
                _subscriptions.RemoveNamed(name);
                await _connection.PublishEventAsync(new MqttSubscriptionChangedEvent(
                    Name,
                    _clock.GetUtcNow(),
                    name,
                    subscribed: false), CancellationToken.None).ConfigureAwait(false);
                return new MqttUnsubscribeResult(_clock.GetUtcNow(), name, changed: true);
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask RemoveTriggerAsync(MqttTriggerRegistration registration)
    {
        if (_connection.IsDisposed)
            return;

        try
        {
            await _connection.ExecuteExclusiveAsync(
                (session, _) => RemoveTriggerCoreAsync(registration, unsubscribe: true, session),
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (ObjectDisposedException) when (_connection.IsDisposed)
        {
        }
    }

    private async ValueTask RemoveTriggerCoreAsync(
        MqttTriggerRegistration registration,
        bool unsubscribe,
        IMqttTransportSession session)
    {
        var inlineIdentities = _subscriptions.RemoveTrigger(registration);
        if (inlineIdentities.Count == 0 || !unsubscribe || !session.IsConnected)
            return;

        List<Exception>? failures = null;
        foreach (var identity in inlineIdentities)
        {
            try
            {
                await session.UnsubscribeAsync(identity, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                (failures ??= []).Add(exception);
            }
        }

        if (failures is not null)
        {
            throw new AggregateException(
                "One or more MQTT inline subscriptions could not be removed.",
                failures);
        }
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(_disposed != 0, this);
}
