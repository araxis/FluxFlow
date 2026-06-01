namespace FluxFlow.Components.Serialization.Contracts;

public sealed record Base64EncodeRequest
{
    public byte[]? Bytes { get; init; }
    public string? Text { get; init; }
    public string? Encoding { get; init; }
    public bool InsertLineBreaks { get; init; }
}
