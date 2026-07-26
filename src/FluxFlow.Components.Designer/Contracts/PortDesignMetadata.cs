using FluxFlow.Composition;

namespace FluxFlow.Components.Designer.Contracts;

public sealed record PortDesignMetadata
{
    public required ComponentPortName Name { get; init; }
    public required PortDirection Direction { get; init; }
    public ComponentMetadataText? DisplayName { get; init; }
    public ComponentPortGroup? Group { get; init; }
    public int Order { get; init; }
    public ComponentMetadataText? Summary { get; init; }
    public ComponentValueTypeHint? ValueType { get; init; }
    public Type? MessageType { get; init; }
    public ComponentPortKind Kind { get; init; } = ComponentPortKind.Message;
    public ComponentPortLinkCardinality LinkCardinality { get; init; } =
        ComponentPortLinkCardinality.Multiple;
    public bool IsPrimary { get; init; }
    public IReadOnlyDictionary<ComponentAttributeName, ComponentAttributeValue> Attributes { get; init; } = new Dictionary<ComponentAttributeName, ComponentAttributeValue>();
}
