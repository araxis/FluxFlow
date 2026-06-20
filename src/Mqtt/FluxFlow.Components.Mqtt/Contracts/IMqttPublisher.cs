namespace FluxFlow.Components.Mqtt.Contracts;

public interface IMqttPublisher
{
    ValueTask PublishAsync(
        MqttPublishRequest request,
        CancellationToken cancellationToken = default);
}
