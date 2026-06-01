namespace FluxFlow.Components.FileSystem.Contracts;

public sealed record FileReadRequest
{
    public required string Path { get; init; }
    public string? Encoding { get; init; }
    public FileReadMode ReadAs { get; init; } = FileReadMode.Text;
}
