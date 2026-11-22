using FluxFlow.Components.Mqtt;
using FluxFlow.Configuration.Application.Links;

namespace FluxFlow.Configuration.Components.Mqtt;

public record MqttComponentConfiguration : ComponentConfiguration
{
    public string Connection { get; init; } = string.Empty;
    public int OutputCapacity { get; init; } = 128;
}

public sealed record MqttSubscriberConfiguration : MqttComponentConfiguration
{
    public MqttSubscriberConfiguration() { }
    public MqttSubscriberConfiguration(IReadOnlyList<MqttTopic> topics) => Topics = topics;

    public MqttAcknowledgementMode AcknowledgementMode { get; init; }
    public int MaxPendingAcknowledgements { get; init; } = 128;
    public OneOrManyPortReferences ACK { get; init; } = OneOrManyPortReferences.Empty;
    // The MQTT registrar reads the scalar/object/array syntax before registering the factory.
    public IReadOnlyList<MqttTopic> Topics { get; internal set; } = Array.Empty<MqttTopic>();
}
