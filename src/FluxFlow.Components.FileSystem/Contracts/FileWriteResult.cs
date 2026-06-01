namespace FluxFlow.Components.FileSystem.Contracts;

public sealed record FileWriteResult
{
    public required string Path { get; init; }
    public required long BytesWritten { get; init; }
    public required FileWriteMode Mode { get; init; }
    public required DateTimeOffset WrittenAt { get; init; }
}
