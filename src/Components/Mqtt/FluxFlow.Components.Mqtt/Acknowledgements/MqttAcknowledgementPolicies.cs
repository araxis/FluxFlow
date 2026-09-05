using System.Text.Json.Serialization;

namespace FluxFlow.Components.Mqtt.Acknowledgements;

[JsonConverter(typeof(JsonStringEnumConverter<MqttWorkflowAcknowledgement>))]
public enum MqttWorkflowAcknowledgement
{
    None = 0,
    Required = 1
}

[JsonConverter(typeof(JsonStringEnumConverter<MqttBrokerAcknowledgement>))]
public enum MqttBrokerAcknowledgement
{
    Automatic = 0,
    AfterHandoff = 1,
    AfterOutcome = 2
}

[JsonConverter(typeof(JsonStringEnumConverter<MqttWorkflowOutcome>))]
public enum MqttWorkflowOutcome
{
    Ack = 0,
    Nak = 1,
    Timeout = 2
}
