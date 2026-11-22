using System.IO.Ports;
using System.Text.Json;
using FluxFlow.Authoring;
using static FluxFlow.Authoring.OptionEditorModel;

namespace FluxFlow.Plugins.Serial;

internal static class SerialPortSchema
{
    internal static PluginConnection Describe() => new("serial.port", "Serial port", "PortName", true,
        JsonSerializer.SerializeToElement(new { Type = "serial.port", PortName = "COM3", BaudRate = 115200 }), [])
    {
        EndpointIsUri = false,
        Sections = [
            new() { Name = "Port", Options = [
                Create("PortName", "Port name", help: "A port on the execution host, such as COM3 or /dev/ttyUSB0.") with
                    { IsRequired = true, Suggestions = new("ports", "No local ports found. You can enter a port name.") },
                Create("BaudRate", "Baud rate", OptionEditorKind.Integer, defaultValue: 115200) with { Min = 1, Max = int.MaxValue },
                Create("DataBits", "Data bits", OptionEditorKind.Integer, defaultValue: 8) with { Min = 5, Max = 8 },
                Select<Parity>("Parity", "Parity", "None"), Select<StopBits>("StopBits", "Stop bits", "One")
            ] },
            new() { Name = "Flow control", Options = [
                Select<Handshake>("Handshake", "Handshake", "None"),
                Create("DtrEnable", "DTR", OptionEditorKind.Toggle, "Assert DTR while the port is open.", false),
                Create("RtsEnable", "RTS", OptionEditorKind.Toggle, "Cannot be manually enabled when hardware flow control owns RTS.", false)
            ] },
            new() { Name = "Timeouts", Options = [
                Create("ReadTimeout", "Read", OptionEditorKind.Duration, "1 millisecond to 60 seconds.", "00:00:00.250") with
                    { MinDuration = TimeSpan.FromMilliseconds(1), MaxDuration = TimeSpan.FromMinutes(1) },
                Create("WriteTimeout", "Write", OptionEditorKind.Duration, "1 millisecond to 2,147,483,647 milliseconds.", "00:00:02") with
                    { MinDuration = TimeSpan.FromMilliseconds(1), MaxDuration = TimeSpan.FromMilliseconds(int.MaxValue) }
            ] }
        ]
    };

    private static OptionEditorModel Select<T>(string name, string label, string value) where T : struct, Enum =>
        Create(name, label, OptionEditorKind.Select, defaultValue: value) with
        { Choices = Enum.GetValues<T>().Where(item => item is not StopBits.None).Select(item => OptionChoiceModel.FromEnum(item)).ToArray() };
}
