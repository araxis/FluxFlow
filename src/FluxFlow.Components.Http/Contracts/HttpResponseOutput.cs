namespace FluxFlow.Components.Http.Contracts;

public sealed record HttpResponseOutput
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public string Method { get; init; } = "";
    public string Url { get; init; } = "";
    public int StatusCode { get; init; }
    public string? ReasonPhrase { get; init; }
    public Dictionary<string, string[]> Headers { get; init; } = [];
    public byte[] BodyBytes { get; init; } = [];
    public string? Body { get; init; }
    public string? ContentType { get; init; }
    public long ElapsedMilliseconds { get; init; }
    public bool Success { get; init; }
    public bool BodyTruncated { get; init; }
}
