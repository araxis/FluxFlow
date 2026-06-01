namespace FluxFlow.Components.Serialization.Contracts;

public sealed record TextEncodeRequest
{
    public string? Text { get; init; }
    public string? Encoding { get; init; }
    public bool EmitBom { get; init; }
}
