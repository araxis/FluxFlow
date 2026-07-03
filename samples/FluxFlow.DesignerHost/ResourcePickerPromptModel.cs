namespace FluxFlow.DesignerHost;

/// <summary>
/// Host-local view of one resource reference a node asks the host to fill: which
/// picker to show, how keys look, and when the reference is required. The host
/// still owns the resource catalog, registration, and lifetimes — this model only
/// describes the prompt.
/// </summary>
public sealed record ResourcePickerPromptModel
{
    public required string ResourceName { get; init; }
    public required string DisplayName { get; init; }
    public required string PickerKind { get; init; }
    public string? Summary { get; init; }
    public string? KeyPattern { get; init; }
    public string? ValueType { get; init; }
    public bool IsRequired { get; init; }
    public string? RelatedOption { get; init; }
    public IReadOnlyList<string> RequiredWhenAnyOptions { get; init; } = [];
}
