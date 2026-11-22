namespace FluxFlow.Configuration.Components.Serial;

public sealed record SerialTextConfiguration : ComponentConfiguration
{
    public string Connection { get; init; } = string.Empty;
    public string NewLine { get; init; } = "\n";
    public int MaxLineBytes { get; init; } = 65536;
    public int InputCapacity { get; init; } = 128;
    public int OutputCapacity { get; init; } = 128;
}
