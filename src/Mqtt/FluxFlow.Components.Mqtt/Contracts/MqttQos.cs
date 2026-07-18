using System.Text.Json.Serialization;

namespace FluxFlow.Components.Mqtt.Contracts;

[JsonConverter(typeof(JsonStringEnumConverter<MqttQos>))]
public enum MqttQos
{
    AtMostOnce = 0,
    AtLeastOnce = 1,
    ExactlyOnce = 2
}
