namespace FluxFlow.Components.Serialization.Options;

public sealed record SerializationNodeOptions
{
    public int BoundedCapacity { get; init; } = 128;
    public string DefaultEncoding { get; init; } = "utf-8";
    public int MaxInputBytes { get; init; } = 1_048_576;
    public int MaxOutputBytes { get; init; } = 1_048_576;
    public bool WriteIndented { get; init; }
    public bool AllowTrailingCommas { get; init; }
    public bool SkipComments { get; init; }
}
