namespace FluxFlow.Environments;

public sealed record ServiceProperty(string Group, string Label, string AccessibleName, string Value,
    Func<string, InfrastructureService> Change)
{
    public bool Toggle { get; init; }
    public bool Secret { get; init; }
    public int Lines { get; init; } = 1;
    public IReadOnlyList<string>? Choices { get; init; }
    public string? Help { get; init; }
}
