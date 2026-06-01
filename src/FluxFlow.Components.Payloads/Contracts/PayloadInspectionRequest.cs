namespace FluxFlow.Components.Payloads.Contracts;

public sealed record PayloadInspectionRequest
{
    public byte[]? Bytes { get; init; }
    public string? Text { get; init; }
    public string? ContentType { get; init; }
    public string? EncodingHint { get; init; }
}
