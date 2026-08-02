namespace FluxFlow.Components.FileSystem.Contracts;

/// <summary>
/// Describes one file-system change emitted by a watcher.
/// </summary>
public sealed record FileChange(
    DateTimeOffset Timestamp,
    string Path,
    string Directory,
    string? Name,
    string ChangeType,
    string? OldPath,
    string? OldName);
