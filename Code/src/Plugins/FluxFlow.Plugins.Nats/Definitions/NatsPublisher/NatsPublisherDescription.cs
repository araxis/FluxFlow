namespace FluxFlow.Authoring.Definitions.NatsPublisher;

public sealed class NatsPublisherDescription : IComponentDescription
{
    public PaletteItemModel Palette { get; } = Nats.NatsDescription.Palette("Publisher", "Publishes dynamic subjects, bytes and headers. Core NATS sends are not durable broker acknowledgements.");
    public NodeInspectorModel Inspector { get; } = Nats.NatsDescription.Inspector("nats.publisher");
}

