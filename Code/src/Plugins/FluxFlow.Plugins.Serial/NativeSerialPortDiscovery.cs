using System.IO.Ports;

using FluxFlow.Environments;

namespace FluxFlow.Execution.Serial;

public sealed class NativeSerialPortDiscovery : ISerialPortDiscovery
{
    public async Task<string[]> ListAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var ports = await Task.Run(SerialPort.GetPortNames, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return ports.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
