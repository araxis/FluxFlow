using FluxFlow.Components.Mqtt.Acknowledgements;
using FluxFlow.Components.Mqtt.Configuration;
using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Components.Mqtt.Subscriptions;
using FluxFlow.Components.Mqtt.Transport;
using System.Collections.Concurrent;
using System.Threading.Channels;

namespace FluxFlow.Components.Mqtt.Tests;

internal sealed class VNextRecordingMqttTransportFactory : IMqttTransportFactory
{
    private readonly Func<VNextRecordingMqttTransportSession> _create;

    public VNextRecordingMqttTransportFactory(Func<VNextRecordingMqttTransportSession>? create = null)
    {
        _create = create ?? (() => new VNextRecordingMqttTransportSession());
    }

    public ConcurrentQueue<VNextRecordingMqttTransportSession> Sessions { get; } = new();

    public ValueTask<IMqttTransportSession> CreateAsync(
        MqttClientConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var session = _create();
        Sessions.Enqueue(session);
        return ValueTask.FromResult<IMqttTransportSession>(session);
    }
}

internal sealed class VNextRecordingMqttTransportSession : IMqttTransportSession
{
    private readonly Channel<MqttTransportReceivedMessage> _messages = Channel.CreateUnbounded<MqttTransportReceivedMessage>();
    private readonly Channel<MqttTransportEvent> _events = Channel.CreateUnbounded<MqttTransportEvent>();
    private int _connectFailuresRemaining;

    public MqttTransportCapabilities Capabilities { get; init; } = new()
    {
        DeferredAcknowledgement = true,
        NegativeAcknowledgement = true
    };

    public bool IsConnected { get; private set; }

    public IAsyncEnumerable<MqttTransportReceivedMessage> Messages => _messages.Reader.ReadAllAsync();

    public IAsyncEnumerable<MqttTransportEvent> Events => _events.Reader.ReadAllAsync();

    public ConcurrentQueue<MqttPublishMessage> Published { get; } = new();

    public ConcurrentQueue<(string Identity, MqttSubscriptionDefinition Subscription)> Subscribed { get; } = new();

    public ConcurrentQueue<string> Unsubscribed { get; } = new();

    public ConcurrentQueue<(MqttTransportDeliveryToken Delivery, MqttWorkflowOutcome Outcome)> Acknowledged { get; } = new();

    public Func<MqttPublishMessage, CancellationToken, ValueTask>? PublishHandler { get; init; }

    public Func<Exception>? ConnectFailure { get; init; }

    public Func<string, MqttSubscriptionDefinition, CancellationToken, ValueTask>? SubscribeHandler { get; init; }

    public int ConnectCalls { get; private set; }

    public int DisconnectCalls { get; private set; }

    public int ConnectFailuresRemaining
    {
        get => Volatile.Read(ref _connectFailuresRemaining);
        set => Volatile.Write(ref _connectFailuresRemaining, value);
    }

    public ValueTask ConnectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ConnectCalls++;
        if (Interlocked.Decrement(ref _connectFailuresRemaining) >= 0)
            throw ConnectFailure?.Invoke() ?? new InvalidOperationException("Broker unavailable.");
        IsConnected = true;
        return ValueTask.CompletedTask;
    }

    public ValueTask DisconnectAsync(
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DisconnectCalls++;
        IsConnected = false;
        return ValueTask.CompletedTask;
    }

    public async ValueTask PublishAsync(
        MqttPublishMessage message,
        CancellationToken cancellationToken = default)
    {
        if (PublishHandler is not null)
            await PublishHandler(message, cancellationToken).ConfigureAwait(false);
        Published.Enqueue(message);
    }

    public async ValueTask SubscribeAsync(
        string identity,
        MqttSubscriptionDefinition subscription,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (SubscribeHandler is not null)
            await SubscribeHandler(identity, subscription, cancellationToken).ConfigureAwait(false);
        Subscribed.Enqueue((identity, subscription));
    }

    public ValueTask UnsubscribeAsync(
        string identity,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Unsubscribed.Enqueue(identity);
        return ValueTask.CompletedTask;
    }

    public ValueTask AcknowledgeAsync(
        MqttTransportDeliveryToken delivery,
        MqttWorkflowOutcome outcome,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Acknowledged.Enqueue((delivery, outcome));
        return ValueTask.CompletedTask;
    }

    public ValueTask EmitAsync(
        MqttReceivedApplicationMessage message,
        string delivery = "delivery-1")
        => _messages.Writer.WriteAsync(new MqttTransportReceivedMessage
        {
            Message = message,
            Delivery = new MqttTransportDeliveryToken(delivery)
        });

    public ValueTask EmitDisconnectedAsync(string message = "Connection lost.")
    {
        IsConnected = false;
        return _events.Writer.WriteAsync(new MqttTransportEvent
        {
            Kind = MqttTransportEventKind.Disconnected,
            Message = message,
            IsTransient = true
        });
    }

    public ValueTask DisposeAsync()
    {
        IsConnected = false;
        _messages.Writer.TryComplete();
        _events.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }
}
