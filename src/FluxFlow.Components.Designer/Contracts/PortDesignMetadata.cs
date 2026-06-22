namespace FluxFlow.Components.Designer.Contracts;

public sealed record PortDesignMetadata
{
    public required ComponentPortName Name { get; init; }
    public required PortDirection Direction { get; init; }
    public string? DisplayName { get; init; }
    public string? Group { get; init; }
    public int Order { get; init; }
    public string? Summary { get; init; }
    public string? ValueType { get; init; }
    public bool IsPrimary { get; init; }
    public IReadOnlyDictionary<string, string> Attributes { get; init; } = new Dictionary<string, string>();
}
