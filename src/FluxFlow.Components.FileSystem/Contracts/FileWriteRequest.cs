namespace FluxFlow.Components.FileSystem.Contracts;

public sealed record FileWriteRequest
{
    public required string Path { get; init; }
    public string? Content { get; init; }
    public byte[]? Bytes { get; init; }
    public string? Encoding { get; init; }
    public FileWriteMode Mode { get; init; } = FileWriteMode.Overwrite;
    public bool CreateDirectories { get; init; } = true;
}
