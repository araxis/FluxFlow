namespace FluxFlow.Components.Designer.Contracts;

public sealed record OptionChoiceMetadata
{
    public required ComponentOptionChoiceValue Value { get; init; }
    public ComponentMetadataText? DisplayName { get; init; }
    public ComponentMetadataText? HelperText { get; init; }
    public IReadOnlyDictionary<ComponentAttributeName, ComponentAttributeValue> Attributes { get; init; } = new Dictionary<ComponentAttributeName, ComponentAttributeValue>();
}
