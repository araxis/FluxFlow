namespace FluxFlow.Components.Designer.Contracts;

public sealed record OptionDesignMetadata
{
    public required ComponentOptionName Name { get; init; }
    public required OptionValueKind Kind { get; init; }
    public string? DisplayName { get; init; }
    public string? HelperText { get; init; }
    public bool IsRequired { get; init; }
    public object? DefaultValue { get; init; }
    public double? Min { get; init; }
    public double? Max { get; init; }
    public IReadOnlyList<OptionChoiceMetadata> Choices { get; init; } = [];
    public IReadOnlyDictionary<ComponentAttributeName, ComponentAttributeValue> Attributes { get; init; } = new Dictionary<ComponentAttributeName, ComponentAttributeValue>();
}
