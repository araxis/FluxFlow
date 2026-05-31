using FluxFlow.Components.Mqtt.Options;

namespace FluxFlow.Components.Mqtt.Contracts;

public interface IMqttClientAdapter : IAsyncDisposable
{
    ValueTask PublishAsync(
        MqttPublishRequest request,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<MqttReceivedMessage> SubscribeAsync(
        MqttSubscriptionOptions options,
        CancellationToken cancellationToken = default);
}
