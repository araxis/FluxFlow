using FluxFlow.Components.Mqtt.Options;

namespace FluxFlow.Components.Mqtt.Contracts;

public interface IMqttTriggerSource
{
    ValueTask<IMqttSubscription> SubscribeAsync(
        MqttTriggerOptions options,
        CancellationToken cancellationToken = default);
}
