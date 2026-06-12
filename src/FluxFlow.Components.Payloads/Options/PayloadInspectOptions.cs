namespace FluxFlow.Components.Payloads.Options;

public sealed record PayloadInspectOptions
{
    public int MaxInputBytes { get; init; } = 1_048_576;
    public int MaxPreviewBytes { get; init; } = 1024;
    public int MaxFormattedChars { get; init; } = 4096;
    public bool DetectBase64 { get; init; } = true;
    public bool FormatJson { get; init; } = true;
    public bool FormatXml { get; init; } = true;
    public int BoundedCapacity { get; init; } = 128;
}
