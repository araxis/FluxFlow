namespace FluxFlow.Authoring.Definitions.JetStreamSettlement;

public sealed class JetStreamSettlementDescription : IComponentDescription
{
    public PaletteItemModel Palette { get; } = JetStream.JetStreamDescription.Palette("Settlement", "Acknowledges, negatively acknowledges, extends or terminates a delivery using its run-scoped settlement token.");
    public NodeInspectorModel Inspector { get; } = JetStream.JetStreamDescription.Inspector("nats.jetstream.settlement");
}
