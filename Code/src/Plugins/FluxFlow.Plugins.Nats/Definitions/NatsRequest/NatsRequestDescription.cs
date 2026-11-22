namespace FluxFlow.Authoring.Definitions.NatsRequest;

public sealed class NatsRequestDescription : IComponentDescription
{
    public PaletteItemModel Palette { get; } = Nats.NatsDescription.Palette("Request", "Sends a dynamic request and awaits one response within a finite deadline.");
    public NodeInspectorModel Inspector { get; } = Nats.NatsDescription.Inspector("nats.request");
}

