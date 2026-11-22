namespace FluxFlow.Authoring.Definitions.SerialReader;

public sealed class SerialReaderDescription : IComponentDescription
{
    public PaletteItemModel Palette { get; } = Serial.SerialFrameDescription.Palette("serial.reader", "Serial Reader", false);
    public NodeInspectorModel Inspector { get; } = Serial.SerialFrameDescription.Inspector("serial.reader");
}
