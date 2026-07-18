using FluxFlow.Components.Mqtt.Acknowledgements;
using FluxFlow.Components.Mqtt.Configuration;
using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Components.Mqtt.Subscriptions;

namespace FluxFlow.Components.Mqtt.Transport;

public interface IMqttTransportFactory
{
    ValueTask<IMqttTransportSession> CreateAsync(
        MqttClientConfiguration configuration,
        CancellationToken cancellationToken = default);
}

public interface IMqttTransportSession : IAsyncDisposable
{
    MqttTransportCapabilities Capabilities { get; }

    bool IsConnected { get; }

    IAsyncEnumerable<MqttTransportReceivedMessage> Messages { get; }

    IAsyncEnumerable<MqttTransportEvent> Events { get; }

    ValueTask ConnectAsync(CancellationToken cancellationToken = default);

    ValueTask DisconnectAsync(string? reason = null, CancellationToken cancellationToken = default);

    ValueTask PublishAsync(
        MqttPublishMessage message,
        CancellationToken cancellationToken = default);

    ValueTask SubscribeAsync(
        string identity,
        MqttSubscriptionDefinition subscription,
        CancellationToken cancellationToken = default);

    ValueTask UnsubscribeAsync(
        string identity,
        CancellationToken cancellationToken = default);

    ValueTask AcknowledgeAsync(
        MqttTransportDeliveryToken delivery,
        MqttWorkflowOutcome outcome,
        CancellationToken cancellationToken = default);
}

public readonly record struct MqttTransportDeliveryToken(string Value)
{
    public bool IsEmpty => string.IsNullOrEmpty(Value);
}

public sealed record MqttTransportReceivedMessage
{
    public required MqttReceivedApplicationMessage Message { get; init; }

    public MqttTransportDeliveryToken Delivery { get; init; }
}

public sealed record MqttTransportEvent
{
    public required MqttTransportEventKind Kind { get; init; }

    public string? Message { get; init; }

    public bool IsTransient { get; init; }
}

public enum MqttTransportEventKind
{
    Connected = 0,
    Disconnected = 1
}
