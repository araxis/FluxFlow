namespace FluxFlow.Components.Mqtt.Diagnostics;

/// <summary>
/// Stable <see cref="FluxFlow.Nodes.FlowEvent.Name"/> values the MQTT nodes emit on
/// their <c>Events</c> ports.
/// </summary>
public static class MqttEventNames
{
    public const string PublishSucceeded = "mqtt.publish.succeeded";
    public const string PublishFailed = "mqtt.publish.failed";
    public const string SubscribeStarted = "mqtt.subscribe.started";
    public const string SubscribeReceived = "mqtt.subscribe.received";
    public const string SubscribeStopped = "mqtt.subscribe.stopped";
    public const string SubscribeFailed = "mqtt.subscribe.failed";
    public const string ConnectionHealthChanged = "mqtt.connection.healthChanged";
}
