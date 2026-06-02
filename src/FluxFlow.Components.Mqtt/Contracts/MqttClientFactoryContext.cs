using FluxFlow.Components.Mqtt.Options;
using FluxFlow.Components.Mqtt.Timing;
using FluxFlow.Engine.Definitions;

namespace FluxFlow.Components.Mqtt.Contracts;

public sealed record MqttClientFactoryContext
{
    public required NodeAddress Address { get; init; }
    public string? ConnectionName { get; init; }
    public required MqttConnectionProfile Profile { get; init; }
    public IMqttClock Clock { get; init; } = SystemMqttClock.Instance;
}
