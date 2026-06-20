using FluxFlow.Components.Mqtt.Contracts;

namespace FluxFlow.Components.Mqtt.PulseMqtt;

internal sealed class PulseMqttReceivedContext(MqttReceivedMessage message) : IMqttReceivedContext
{
    public MqttReceivedMessage Message { get; } = message;

    public ValueTask AckAsync(CancellationToken cancellationToken = default)
        => ValueTask.CompletedTask;

    public ValueTask NackAsync(
        Exception? error = null,
        CancellationToken cancellationToken = default)
        => ValueTask.CompletedTask;
}
