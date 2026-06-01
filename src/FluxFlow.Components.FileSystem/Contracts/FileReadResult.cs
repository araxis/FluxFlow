namespace FluxFlow.Components.FileSystem.Contracts;

public sealed record FileReadResult
{
    public required string Path { get; init; }
    public string? Content { get; init; }
    public byte[]? Bytes { get; init; }
    public string? Encoding { get; init; }
    public required long BytesRead { get; init; }
    public required FileReadMode ReadAs { get; init; }
    public required DateTimeOffset ReadAt { get; init; }
}
