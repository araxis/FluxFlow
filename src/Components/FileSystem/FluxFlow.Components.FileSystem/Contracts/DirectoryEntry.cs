namespace FluxFlow.Components.FileSystem.Contracts;

/// <summary>
/// Describes one entry emitted by a directory enumeration.
/// </summary>
public sealed record DirectoryEntry(
    DateTimeOffset EnumeratedAt,
    string Path,
    string Directory,
    string Name,
    string EntryType,
    long? Length,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? LastModifiedAt,
    FileAttributes Attributes);
