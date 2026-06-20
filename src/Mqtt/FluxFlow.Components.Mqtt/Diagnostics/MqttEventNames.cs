namespace FluxFlow.Components.Mqtt.Diagnostics;

/// <summary>
/// Stable <see cref="FluxFlow.Nodes.FlowEvent.Name"/> values for MQTT node and
/// adapter-owned health events.
/// </summary>
public static class MqttEventNames
{
    public const string PublishSucceeded = "mqtt.publish.succeeded";
    public const string PublishFailed = "mqtt.publish.failed";
    public const string TriggerStarted = "mqtt.trigger.started";
    public const string TriggerReceived = "mqtt.trigger.received";
    public const string TriggerStopped = "mqtt.trigger.stopped";
    public const string TriggerFailed = "mqtt.trigger.failed";
    public const string TriggerAcknowledged = "mqtt.trigger.acknowledged";
    public const string TriggerRejected = "mqtt.trigger.rejected";
    public const string TriggerResponseSucceeded = "mqtt.trigger.response.succeeded";
    public const string TriggerResponseFailed = "mqtt.trigger.response.failed";
    public const string TriggerResponseIgnored = "mqtt.trigger.response.ignored";
    public const string ClientHealthChanged = "mqtt.client.healthChanged";
}
