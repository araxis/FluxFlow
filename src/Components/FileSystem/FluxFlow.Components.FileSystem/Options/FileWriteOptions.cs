namespace FluxFlow.Components.FileSystem.Options;

public sealed record FileWriteOptions
{
    public int BoundedCapacity { get; init; } = 128;
    public string? BaseDirectory { get; init; }
    public bool AllowAbsolutePaths { get; init; }
    public string DefaultEncoding { get; init; } = "utf-8";
}
