namespace FluxFlow.Components.Http.Contracts;

public sealed record HttpRequestInput
{
    public string Method { get; init; } = "GET";
    public string? Url { get; init; }
    public Dictionary<string, string> Headers { get; init; } = [];
    public string? Body { get; init; }
    public byte[]? Bytes { get; init; }
    public string? ContentType { get; init; }
    public int? TimeoutMilliseconds { get; init; }
}
