using FluxFlow.Configuration.Components.Serial;
using static FluxFlow.Authoring.OptionEditorModel;

namespace FluxFlow.Authoring.Definitions.SerialText;

public sealed class SerialTextDescription : IComponentDescription
{
    public PaletteItemModel Palette { get; } = new()
    {
        ComponentType = "serial.text", DisplayName = "Serial Text", Category = "Devices",
        PreferredNodeName = "serial-text",
        Icon = """<path d="M0 0h24v24H0V0z" fill="none"/><path d="M15 7v4h1v2h-3V5h2l-3-4-3 4h2v8H8v-2.07c.7-.37 1.2-1.08 1.2-1.93 0-1.21-.99-2.2-2.2-2.2S4.8 7.79 4.8 9c0 .85.5 1.56 1.2 1.93V13c0 1.11.89 2 2 2h3v3.05c-.71.37-1.2 1.1-1.2 1.95 0 1.22.99 2.2 2.2 2.2s2.2-.98 2.2-2.2c0-.85-.49-1.58-1.2-1.95V15h3c1.11 0 2-.89 2-2v-2h1V7h-4z"/>""",
        DefaultColor = "#66b3e0",
        Summary = "Reads and writes newline-delimited UTF-8 text. Each serial port has one workflow-local Serial Text owner.",
        Inputs = [new() { Name = "Input", Kind = PortKind.Input }],
        Outputs = [new() { Name = "Output", Kind = PortKind.Output },
            new() { Name = "Events", Kind = PortKind.Output, ColorRole = "events" }]
    };

    public NodeInspectorModel Inspector { get; } = new()
    {
        ComponentType = "serial.text",
        Sections = [new()
        {
            Name = "Configuration",
            Options =
            [
                Create(nameof(SerialTextConfiguration.Connection), "Connection", OptionEditorKind.Text,
                    "Reference to a serial.port resource in this workflow. Port name and baud rate belong to that resource.") with { IsRequired = true, RelatedResource = "serial.port" },
                Create(nameof(SerialTextConfiguration.NewLine), "New line", OptionEditorKind.Select,
                    "Line ending used for serial text. The runtime supports LF and CRLF.", defaultValue: "\n") with
                {
                    Choices = [new() { Value = "\n", DisplayName = "LF" }, new() { Value = "\r\n", DisplayName = "CRLF" }]
                },
                Create(nameof(SerialTextConfiguration.MaxLineBytes), "Max line bytes", OptionEditorKind.Integer,
                    "Maximum incoming line size in bytes.", defaultValue: 65536) with { Min = 1, Max = int.MaxValue - 1 },
                Create(nameof(SerialTextConfiguration.InputCapacity), "Input capacity", OptionEditorKind.Integer,
                    "Maximum queued write commands.", defaultValue: 128) with { Min = 1, Max = int.MaxValue },
                Create(nameof(SerialTextConfiguration.OutputCapacity), "Output capacity", OptionEditorKind.Integer,
                    "Maximum messages buffered at the output. Must be greater than zero.", defaultValue: 128) with { Min = 1, Max = int.MaxValue }
            ]
        }]
    };
}

