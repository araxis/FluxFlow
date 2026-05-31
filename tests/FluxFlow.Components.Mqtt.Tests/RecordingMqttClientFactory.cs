using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Components.Mqtt.Options;

namespace FluxFlow.Components.Mqtt.Tests;

internal sealed class RecordingMqttClientFactory(RecordingMqttClientAdapter adapter) : IMqttClientFactory
{
    public List<MqttConnectionProfile> Connections { get; } = [];

    public ValueTask<IMqttClientAdapter> CreateAsync(
        MqttConnectionProfile connection,
        CancellationToken cancellationToken = default)
    {
        Connections.Add(connection);
        return ValueTask.FromResult<IMqttClientAdapter>(adapter);
    }
}
