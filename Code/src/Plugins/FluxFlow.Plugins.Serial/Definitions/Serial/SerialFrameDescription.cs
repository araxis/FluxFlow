using FluxFlow.Components.Serial;
using static FluxFlow.Authoring.OptionEditorModel;

namespace FluxFlow.Authoring.Definitions.Serial;

public static class SerialFrameDescription
{
    public static PaletteItemModel Palette(string type, string name, bool writer) => new()
    {
        ComponentType = type, DisplayName = name, Category = "Devices", PreferredNodeName = writer ? "serial-writer" : "serial-reader",
        Icon = new SerialText.SerialTextDescription().Palette.Icon, DefaultColor = "#66b3e0",
        Summary = writer ? "Writes one Bytes array (integers 0–255), DataBase64 or Text per input. Commands are never retried automatically."
            : "Reads raw chunks or configured frames. Preserves original bytes and optionally decodes text.",
        Inputs = writer ? [new() { Name = "Input", Kind = PortKind.Input }] : [],
        Outputs = [new() { Name = "Output", Kind = PortKind.Output }, new() { Name = "Events", Kind = PortKind.Output, ColorRole = "events" }]
    };
    public static NodeInspectorModel Inspector(string type) => new()
    {
        ComponentType = type, PrimaryEditor = "Framing", Sections = [new() { Name = "Serial frames", Options = OptionsFor(type) }]
    };
    public static IReadOnlyList<OptionEditorModel> Options { get; } = [
        Create("Connection", "Connection", help: "A serial.port resource in this workflow. Reader and Writers share the same opened port.") with { IsRequired = true, RelatedResource = "serial.port" },
        Create("Framing", "Framing", OptionEditorKind.Select, "Raw chunks, delimiter-terminated payloads, or an exact byte count.", "Delimiter") with { Choices = Choices<SerialFraming>() },
        Create("DelimiterBase64", "Delimiter", OptionEditorKind.Base64, help: "Delimiter bytes, excluded from received payloads and appended to writes. Choose a line ending or enter 1 to 64 custom bytes as Base64.", defaultValue: "Cg==") with
        {
            VisibleWhen = new("Framing", "Delimiter"), Min = 1, Max = 64,
            Choices = [new() { Value = "Cg==", DisplayName = "LF" }, new() { Value = "DQo=", DisplayName = "CRLF" }, new() { Value = "DQ==", DisplayName = "CR" }]
        },
        Create("FrameLength", "Frame bytes", OptionEditorKind.Integer, "Used only for FixedLength framing.", 1) with { VisibleWhen = new("Framing", "FixedLength"), Min = 1, Max = SerialFrameOptions.FrameSizeLimit },
        Create("Encoding", "Text encoding", OptionEditorKind.Select, "None skips text decoding on reads. For writes with None, supply Bytes or DataBase64.", "Utf8") with { Choices = Choices<SerialTextEncoding>() },
        Create("MaxFrameBytes", "Max frame bytes", OptionEditorKind.Integer, "Oversized frames fail explicitly; data is never truncated.", 65536) with { Min = 1, Max = SerialFrameOptions.FrameSizeLimit },
        Create("ReadBufferBytes", "Read buffer", OptionEditorKind.Integer, "Native read size in bytes. Reader and Writer both require it to fit within Max frame bytes.", 1024) with { Min = 1, Max = SerialFrameOptions.ReadBufferLimit },
        Create("Capacity", "Buffer size", OptionEditorKind.Integer, "Bounded workflow message capacity.", 128) with { Min = 1, Max = 65536 }
    ];
    private static readonly IReadOnlyList<OptionEditorModel> ReaderOptions = [.. Options,
        Create("OutputFormat", "Output format", OptionEditorKind.Select, "Base64 returns DataBase64. Bytes returns a numeric array you can index in expressions.", "Base64")
            with { Choices = Choices<SerialFrameOutputFormat>() }];
    public static IReadOnlyList<OptionEditorModel> OptionsFor(string type) => type == "serial.reader" ? ReaderOptions : Options;
    private static OptionChoiceModel[] Choices<T>() where T : struct, Enum => Enum.GetValues<T>()
        .Select(value => OptionChoiceModel.FromEnum(value)).ToArray();
}
