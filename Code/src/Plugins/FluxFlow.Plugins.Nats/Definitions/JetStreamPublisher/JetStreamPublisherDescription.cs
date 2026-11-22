namespace FluxFlow.Authoring.Definitions.JetStreamPublisher;

public sealed class JetStreamPublisherDescription : IComponentDescription
{
    public PaletteItemModel Palette { get; } = JetStream.JetStreamDescription.Palette("Publisher", "Publishes bytes and headers to a named stream and returns the broker acknowledgement. No automatic publication retry.");
    public NodeInspectorModel Inspector { get; } = JetStream.JetStreamDescription.Inspector("nats.jetstream.publisher");
}
