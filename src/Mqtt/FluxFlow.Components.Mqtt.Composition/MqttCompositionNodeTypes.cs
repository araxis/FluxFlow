namespace FluxFlow.Components.Mqtt.Composition;

public static class MqttCompositionNodeTypes
{
    public const string Control = "mqtt.command";
    public const string LegacyControl = "mqtt.control";

    public const string Publish = "mqtt.publish";

    public const string Trigger = "mqtt.receive";
    public const string LegacyTrigger = "mqtt.trigger";

    public const string Events = "mqtt.events";
}
