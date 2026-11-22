namespace FluxFlow.Authoring.Definitions.NatsReply;

public sealed class NatsReplyDescription : IComponentDescription
{
    public PaletteItemModel Palette { get; } = Nats.NatsDescription.Palette("Reply", "Publishes response bytes and headers to the ReplyTo inbox supplied by a received message.");
    public NodeInspectorModel Inspector { get; } = Nats.NatsDescription.Inspector("nats.reply");
}

