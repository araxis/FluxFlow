using System.IO.Ports;

namespace FluxFlow.Configuration.Components.Serial;

public sealed class SerialPortConfiguration
{
    public required string PortName { get; init; }
    public int BaudRate { get; init; } = 115200;
    public int DataBits { get; init; } = 8;
    public Parity Parity { get; init; } = Parity.None;
    public StopBits StopBits { get; init; } = StopBits.One;
    public Handshake Handshake { get; init; } = Handshake.None;
    public bool DtrEnable { get; init; }
    public bool RtsEnable { get; init; }
    public TimeSpan ReadTimeout { get; init; } = TimeSpan.FromMilliseconds(250);
    public TimeSpan WriteTimeout { get; init; } = TimeSpan.FromSeconds(2);
}
