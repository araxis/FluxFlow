using FluxFlow.Engine.Definitions;

namespace FluxFlow.Components.Mqtt;

public static class MqttComponentTypes
{
    public static readonly NodeType Connection = new("mqtt.connection");
    public static readonly NodeType Subscribe = new("mqtt.subscribe");
    public static readonly NodeType Publish = new("mqtt.publish");
}
