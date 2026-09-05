namespace FluxFlow.Components.FileSystem.Options;

public sealed record FileReadOptions
{
    public const long DefaultMaxBytes = 16_777_216;

    public int BoundedCapacity { get; init; } = 128;
    public string? BaseDirectory { get; init; }
    public bool AllowAbsolutePaths { get; init; }
    public string DefaultEncoding { get; init; } = "utf-8";

    /// <summary>
    /// Maximum file size the node will read, in bytes. Defaults to
    /// <see cref="DefaultMaxBytes"/> (16 MiB); set to <c>null</c> for unlimited reads.
    /// </summary>
    public long? MaxBytes { get; init; } = DefaultMaxBytes;
}
