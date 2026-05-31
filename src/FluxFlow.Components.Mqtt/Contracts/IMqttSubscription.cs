namespace FluxFlow.Components.Mqtt.Contracts;

public interface IMqttSubscription : IAsyncDisposable
{
    IAsyncEnumerable<MqttReceivedMessage> Messages { get; }
}
