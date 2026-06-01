namespace FluxFlow.Components.Serialization.Contracts;

public sealed record JsonParseRequest
{
    public string? Text { get; init; }
    public byte[]? Bytes { get; init; }
    public string? Encoding { get; init; }
    public string? ContentType { get; init; }
}
