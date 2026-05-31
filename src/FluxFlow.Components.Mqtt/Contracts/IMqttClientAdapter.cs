using FluxFlow.Components.Mqtt.Options;

namespace FluxFlow.Components.Mqtt.Contracts;

public interface IMqttClientAdapter : IAsyncDisposable
{
    ValueTask PublishAsync(
        MqttPublishRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<IMqttSubscription> SubscribeAsync(
        MqttSubscriptionOptions options,
        CancellationToken cancellationToken = default);
}
