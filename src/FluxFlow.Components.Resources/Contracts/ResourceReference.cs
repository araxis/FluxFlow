namespace FluxFlow.Components.Resources.Contracts;

public sealed record ResourceReference
{
    public required ResourceName Name { get; init; }
    public string? Kind { get; init; }
    public IReadOnlyDictionary<string, string> Attributes { get; init; } = new Dictionary<string, string>();
}
