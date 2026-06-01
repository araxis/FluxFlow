namespace FluxFlow.Components.Serialization.Contracts;

public sealed record TextDecodeRequest
{
    public byte[]? Bytes { get; init; }
    public string? Encoding { get; init; }
}
