namespace FluxFlow.Components.FileSystem.Contracts;

public sealed record DirectoryEnumerateEntry
{
    public required DateTimeOffset EnumeratedAt { get; init; }
    public required string Path { get; init; }
    public required string Directory { get; init; }
    public required string Name { get; init; }
    public required DirectoryEntryType EntryType { get; init; }
    public long? Length { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public DateTimeOffset? LastModifiedAt { get; init; }
    public required FileAttributes Attributes { get; init; }
}
