namespace FluxFlow.Components.Designer.Contracts;

public sealed record ResourceDesignMetadata
{
    public required ComponentResourceName Name { get; init; }
    public string? DisplayName { get; init; }
    public int Order { get; init; }
    public string? Summary { get; init; }
    public ComponentValueTypeHint? ValueType { get; init; }
    public bool IsRequired { get; init; }
    public IReadOnlyDictionary<ComponentAttributeName, ComponentAttributeValue> Attributes { get; init; } = new Dictionary<ComponentAttributeName, ComponentAttributeValue>();
}
