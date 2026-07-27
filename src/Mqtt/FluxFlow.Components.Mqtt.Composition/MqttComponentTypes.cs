namespace FluxFlow.Components.Mqtt.Composition;

public static class MqttComponentTypes
{
    public const string Control = "mqtt.command";
    public const string Publish = "mqtt.publish";

    public const string Trigger = "mqtt.receive";
    public const string Events = "mqtt.events";
}
