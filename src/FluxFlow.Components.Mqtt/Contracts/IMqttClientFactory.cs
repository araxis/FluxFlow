using FluxFlow.Components.Mqtt.Options;

namespace FluxFlow.Components.Mqtt.Contracts;

public interface IMqttClientFactory
{
    ValueTask<MqttClientLease> CreateAsync(
        MqttClientFactoryContext context,
        CancellationToken cancellationToken = default);
}
