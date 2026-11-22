namespace FluxFlow.Authoring;

/// <summary>
/// Inspector sections and fields in their declared display order, with an optional primary editor.
/// </summary>
public sealed record NodeInspectorModel
{
    public required string ComponentType { get; init; }
    public string? PrimaryEditor { get; init; }
    public IReadOnlyList<OptionSectionModel> Sections { get; init; } = [];
    public IReadOnlyList<ResourcePickerPromptModel> ResourcePrompts { get; init; } = [];
}

public sealed record OptionSectionModel
{
    public required string Name { get; init; }
    public IReadOnlyList<OptionEditorModel> Options { get; init; } = [];
}
