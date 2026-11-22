namespace FluxFlow.Authoring.Definitions.SerialWriter;

public sealed class SerialWriterDescription : IComponentDescription
{
    public PaletteItemModel Palette { get; } = Serial.SerialFrameDescription.Palette("serial.writer", "Serial Writer", true);
    public NodeInspectorModel Inspector { get; } = Serial.SerialFrameDescription.Inspector("serial.writer");
}
