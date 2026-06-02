namespace FluxFlow.Components.Mqtt.Contracts;

public interface IMqttClientHealthSource
{
    IAsyncEnumerable<MqttClientHealthEvent> Health { get; }
}
