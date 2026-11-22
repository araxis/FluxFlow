using System.Text.Json.Nodes;

namespace FluxFlow.Environments;

public sealed class SerialEnvironmentAdapter : IResourceEnvironmentAdapter
{
    public InfrastructureServiceType Infrastructure { get; } = new("serial.port", "Serial device", "serial-port://local/COM3", "Physical port on this execution host, not a TCP endpoint. Use serial-port://local/COM3 or serial-port://local/%2Fdev%2FttyUSB0. Binding replaces only PortName; line settings stay in the project.",
            uri => uri.Scheme == "serial-port" && uri.Host == "local" && uri.Port == -1 && uri.Query.Length == 0 &&
                uri.AbsolutePath.Length > 1 && !string.IsNullOrWhiteSpace(Uri.UnescapeDataString(uri.AbsolutePath[1..])) &&
                !Uri.UnescapeDataString(uri.AbsolutePath[1..]).Any(char.IsControl)) { RequiresApplicationHost = true };
    public string? ExclusiveKey(Uri endpoint)
    {
        var port = Uri.UnescapeDataString(endpoint.AbsolutePath[1..]).Trim();
        return OperatingSystem.IsWindows() ? (port.StartsWith(@"\\.\", StringComparison.Ordinal) ? port[4..] : port).ToUpperInvariant() : port;
    }

    public string Type => "serial.port";
    public string? Endpoint(JsonObject resource) => ResourceJson.Text(resource, "PortName") is { Length: > 0 } port
        ? "serial-port://local/" + Uri.EscapeDataString(port) : null;
    public void Apply(JsonObject resource, Uri endpoint)
    {
        new InfrastructureService { Type = Type, Endpoint = endpoint.AbsoluteUri }.Validate(Infrastructure);
        ResourceJson.Set(resource, "PortName", JsonValue.Create(Uri.UnescapeDataString(endpoint.AbsolutePath[1..])));
    }
}
