namespace FluxFlow.Components.Mqtt.RequestReply;

/// <summary>
/// Publishes a reply back to the requester. The host implements this over its MQTT
/// client (MQTTnet, …) — the trigger never references an MQTT library, exactly as the
/// HTTP node never references a server.
/// </summary>
public interface IMqttResponsePublisher
{
    Task PublishAsync(MqttResponseMessage message, CancellationToken cancellationToken = default);
}
