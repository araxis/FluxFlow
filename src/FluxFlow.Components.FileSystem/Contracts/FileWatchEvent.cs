namespace FluxFlow.Components.FileSystem.Contracts;

public sealed record FileWatchEvent
{
    public required DateTimeOffset Timestamp { get; init; }
    public required string Path { get; init; }
    public required string Directory { get; init; }
    public string? Name { get; init; }
    public required FileWatchChangeType ChangeType { get; init; }
    public string? OldPath { get; init; }
    public string? OldName { get; init; }
}
