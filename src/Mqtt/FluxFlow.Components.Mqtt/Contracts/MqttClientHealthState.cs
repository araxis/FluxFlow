namespace FluxFlow.Components.Mqtt.Contracts;

public enum MqttClientHealthState
{
    Unknown = 0,
    Connecting = 1,
    Connected = 2,
    Reconnecting = 3,
    Disconnected = 4,
    Faulted = 5
}
