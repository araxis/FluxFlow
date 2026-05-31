namespace FluxFlow.Components.Mqtt.Contracts;

public enum MqttQualityOfService
{
    AtMostOnce = 0,
    AtLeastOnce = 1,
    ExactlyOnce = 2
}
