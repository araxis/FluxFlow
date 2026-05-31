using FluxFlow.Components.Mqtt.Contracts;

namespace FluxFlow.Components.Mqtt.Tests;

internal sealed class RecordingMqttClientFactory(
    RecordingMqttClientAdapter adapter,
    bool disposeAdapter = true) : IMqttClientFactory
{
    public List<MqttClientFactoryContext> Contexts { get; } = [];

    public ValueTask<MqttClientLease> CreateAsync(
        MqttClientFactoryContext context,
        CancellationToken cancellationToken = default)
    {
        Contexts.Add(context);
        return ValueTask.FromResult(disposeAdapter
            ? MqttClientLease.Owned(adapter)
            : MqttClientLease.Shared(adapter));
    }
}
