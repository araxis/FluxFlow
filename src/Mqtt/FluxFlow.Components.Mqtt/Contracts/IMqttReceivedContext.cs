namespace FluxFlow.Components.Mqtt.Contracts;

public interface IMqttReceivedContext
{
    MqttReceivedMessage Message { get; }

    ValueTask AckAsync(CancellationToken cancellationToken = default);

    ValueTask NackAsync(
        Exception? error = null,
        CancellationToken cancellationToken = default);
}
