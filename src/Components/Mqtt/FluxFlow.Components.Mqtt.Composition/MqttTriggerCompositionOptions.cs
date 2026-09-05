using System.Text.Json;
using FluxFlow.Components.Mqtt.Acknowledgements;

namespace FluxFlow.Components.Mqtt.Composition;

public sealed record MqttTriggerCompositionOptions
{
    public required JsonElement Subscription { get; init; }

    public MqttWorkflowAcknowledgement WorkflowAcknowledgement { get; init; }

    public MqttBrokerAcknowledgement BrokerAcknowledgement { get; init; } =
        MqttBrokerAcknowledgement.Automatic;

    public TimeSpan OutcomeTimeout { get; init; } = TimeSpan.FromSeconds(30);

    public int MaximumPendingMessages { get; init; } = 128;
}
