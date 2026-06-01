namespace FluxFlow.Components.FileSystem.Options;

public sealed record FileWatchOptions
{
    public int BoundedCapacity { get; init; } = 128;
    public string Directory { get; init; } = string.Empty;
    public string? BaseDirectory { get; init; }
    public bool AllowAbsolutePaths { get; init; }
    public string Filter { get; init; } = "*";
    public bool IncludeSubdirectories { get; init; }
    public string[] NotifyFilters { get; init; } = [];
}
