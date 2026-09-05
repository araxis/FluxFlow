using FluxFlow.Data;

namespace FluxFlow.Components.FileSystem.Contracts;

public sealed record FileReadContent
{
    public required string Path { get; init; }

    public required FlowContent Content { get; init; }

    public required long BytesRead { get; init; }

    public required FileReadMode ReadAs { get; init; }

    public required DateTimeOffset ReadAt { get; init; }
}
