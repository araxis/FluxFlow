namespace FluxFlow.Components.Mqtt.Composition;

public sealed record MqttEventsCompositionOptions
{
    public int MaximumPendingEvents { get; init; } = 128;
}
