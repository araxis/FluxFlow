using FluxFlow.Data;

namespace FluxFlow.Components.FileSystem.Contracts;

public sealed record FileContentWriteRequest
{
    public required string Path { get; init; }

    public required FlowContent Content { get; init; }

    public FileWriteMode Mode { get; init; } = FileWriteMode.Overwrite;

    public bool CreateDirectories { get; init; } = true;
}
