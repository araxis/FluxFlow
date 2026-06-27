namespace FluxFlow.Components.Designer.Contracts;

public sealed record PortDesignMetadata
{
    public required ComponentPortName Name { get; init; }
    public required PortDirection Direction { get; init; }
    public string? DisplayName { get; init; }
    public ComponentPortGroup? Group { get; init; }
    public int Order { get; init; }
    public string? Summary { get; init; }
    public ComponentValueTypeHint? ValueType { get; init; }
    public bool IsPrimary { get; init; }
    public IReadOnlyDictionary<ComponentAttributeName, ComponentAttributeValue> Attributes { get; init; } = new Dictionary<ComponentAttributeName, ComponentAttributeValue>();
}
