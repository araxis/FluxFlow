namespace FluxFlow.Components.Http.Contracts;

public sealed record HttpRequestSendContext
{
    public required HttpRequestInput Input { get; init; }
    public required string Method { get; init; }
    public required Uri Url { get; init; }
    public Dictionary<string, string> Headers { get; init; } = [];
    public byte[]? BodyBytes { get; init; }
    public string? BodyText { get; init; }
    public string? ContentType { get; init; }
    public required TimeSpan Timeout { get; init; }
    public required int MaxResponseBodyBytes { get; init; }
}
