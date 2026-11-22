namespace FluxFlow.Authoring.Definitions.JetStreamConsumer;

public sealed class JetStreamConsumerDescription : IComponentDescription
{
    public PaletteItemModel Palette { get; } = JetStream.JetStreamDescription.Palette("Consumer", "Pulls durable stream deliveries with bounded in-flight work. Acknowledge explicitly after downstream processing.");
    public NodeInspectorModel Inspector { get; } = JetStream.JetStreamDescription.Inspector("nats.jetstream.consumer");
}
