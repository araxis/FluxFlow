namespace FluxFlow.Components.Designer.Contracts;

public sealed record OptionChoiceMetadata
{
    public required ComponentOptionChoiceValue Value { get; init; }
    public string? DisplayName { get; init; }
    public string? HelperText { get; init; }
    public IReadOnlyDictionary<string, string> Attributes { get; init; } = new Dictionary<string, string>();
}
