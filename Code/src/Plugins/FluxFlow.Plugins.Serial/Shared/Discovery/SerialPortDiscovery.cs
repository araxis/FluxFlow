namespace FluxFlow.Environments;

public interface ISerialPortDiscovery
{
    Task<string[]> ListAsync(CancellationToken cancellationToken);
}

public sealed record SerialPortList(string[] Ports, string? Error = null);

[System.Text.Json.Serialization.JsonSerializable(typeof(SerialPortList))]
public partial class SerialDiscoveryJson : System.Text.Json.Serialization.JsonSerializerContext { }
