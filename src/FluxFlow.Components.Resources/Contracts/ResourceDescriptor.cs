namespace FluxFlow.Components.Resources.Contracts;

public sealed record ResourceDescriptor
{
    public required ResourceName Name { get; init; }
    public string? Kind { get; init; }
    public string? DisplayName { get; init; }
    public string? Summary { get; init; }
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}
