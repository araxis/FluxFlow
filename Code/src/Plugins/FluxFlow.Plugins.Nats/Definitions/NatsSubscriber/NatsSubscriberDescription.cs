namespace FluxFlow.Authoring.Definitions.NatsSubscriber;

public sealed class NatsSubscriberDescription : IComponentDescription
{
    public PaletteItemModel Palette { get; } = Nats.NatsDescription.Palette("Subscriber", "Receives Core NATS subjects or wildcard subscriptions, with optional queue-group delivery.");
    public NodeInspectorModel Inspector { get; } = Nats.NatsDescription.Inspector("nats.subscriber");
}

