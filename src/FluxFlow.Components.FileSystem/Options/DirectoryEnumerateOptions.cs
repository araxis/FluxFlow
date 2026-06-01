namespace FluxFlow.Components.FileSystem.Options;

public sealed record DirectoryEnumerateOptions
{
    public int BoundedCapacity { get; init; } = 128;
    public string Directory { get; init; } = string.Empty;
    public string Filter { get; init; } = "*";
    public bool IncludeSubdirectories { get; init; }
    public bool IncludeFiles { get; init; } = true;
    public bool IncludeDirectories { get; init; }
    public string? BaseDirectory { get; init; }
    public bool AllowAbsolutePaths { get; init; }
    public long? MaxEntries { get; init; }
}
