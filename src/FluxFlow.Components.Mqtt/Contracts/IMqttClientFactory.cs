using FluxFlow.Components.Mqtt.Options;

namespace FluxFlow.Components.Mqtt.Contracts;

public interface IMqttClientFactory
{
    ValueTask<IMqttClientAdapter> CreateAsync(
        MqttConnectionProfile connection,
        CancellationToken cancellationToken = default);
}
