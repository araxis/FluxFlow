namespace FluxFlow.DesignerHost;

/// <summary>
/// Host-local view of one option ready for an editor row: which editor to render,
/// the label/help text, and the constraints the metadata declared. The editor kind
/// is already resolved (including the conservative fallback), so renderer code
/// switches on <see cref="Editor"/> and never re-interprets metadata attributes.
/// </summary>
public sealed record OptionEditorModel
{
    public required string Name { get; init; }
    public required string DisplayName { get; init; }
    public required OptionEditorKind Editor { get; init; }
    public string? HelperText { get; init; }
    public string? Syntax { get; init; }
    public bool IsRequired { get; init; }
    public bool IsAdvanced { get; init; }
    public object? DefaultValue { get; init; }
    public double? Min { get; init; }
    public double? Max { get; init; }
    public IReadOnlyList<OptionChoiceModel> Choices { get; init; } = [];
    public string? RelatedResource { get; init; }
}

public sealed record OptionChoiceModel
{
    public required string Value { get; init; }
    public required string DisplayName { get; init; }
    public string? HelperText { get; init; }
}

/// <summary>
/// The editors this host can render. Metadata editor hints and value kinds map to
/// exactly one member; anything unknown falls back to <see cref="Text"/>.
/// </summary>
public enum OptionEditorKind
{
    Text,
    MultilineText,
    Number,
    Toggle,
    Select,
    Duration,
    Secret,
    Expression,
    Json
}
