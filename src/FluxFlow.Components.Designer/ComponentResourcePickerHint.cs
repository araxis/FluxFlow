using FluxFlow.Components.Designer.Contracts;

namespace FluxFlow.Components.Designer;

public sealed record ComponentResourcePickerHint
{
    public required ComponentType ComponentType { get; init; }
    public required ComponentResourceName ResourceName { get; init; }
    public required string PickerKind { get; init; }
    public string? KeyPattern { get; init; }
    public ComponentOptionName? RelatedOption { get; init; }
    public IReadOnlyList<ComponentOptionName> RequiredWhenAnyOptions { get; init; } = [];
    public bool IsRequired { get; init; }
    public ComponentValueTypeHint? ValueType { get; init; }
    public ComponentMetadataText? DisplayName { get; init; }
    public ComponentMetadataText? Summary { get; init; }
}
