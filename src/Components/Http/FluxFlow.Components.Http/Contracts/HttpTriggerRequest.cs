namespace FluxFlow.Components.Http.Contracts;

/// <summary>
/// An inbound HTTP request surfaced by an HTTP trigger. Transport-agnostic: a server
/// adapter (ASP.NET Core, HttpListener, …) maps its native request onto this.
/// </summary>
public sealed record HttpTriggerRequest
{
    public required string Method { get; init; }
    public required string Path { get; init; }
    public string? QueryString { get; init; }
    public IReadOnlyDictionary<string, string[]> Headers { get; init; } =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
    public byte[]? Body { get; init; }
    public string? ContentType { get; init; }
}
