using System.Text.Json.Serialization;

namespace FluxFlow.Components.Mqtt;

[JsonConverter(typeof(JsonStringEnumConverter<MqttAcknowledgementMode>))]
public enum MqttAcknowledgementMode { Automatic, Manual }
