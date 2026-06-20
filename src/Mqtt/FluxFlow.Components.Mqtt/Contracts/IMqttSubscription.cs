namespace FluxFlow.Components.Mqtt.Contracts;

public interface IMqttSubscription : IAsyncDisposable
{
    IAsyncEnumerable<IMqttReceivedContext> Messages { get; }
}
