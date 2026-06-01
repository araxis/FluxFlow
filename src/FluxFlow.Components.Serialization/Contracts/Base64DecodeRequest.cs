namespace FluxFlow.Components.Serialization.Contracts;

public sealed record Base64DecodeRequest
{
    public string? Text { get; init; }
    public string? Encoding { get; init; }
    public bool DecodeText { get; init; }
    public bool AllowWhitespace { get; init; } = true;
}
