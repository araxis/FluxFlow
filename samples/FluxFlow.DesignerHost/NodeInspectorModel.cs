namespace FluxFlow.DesignerHost;

/// <summary>
/// Host-local view of one component's inspector: option sections in display order
/// plus the resource references the node needs. Sections keep metadata order of
/// first appearance; within a section, primary options come before advanced ones.
/// </summary>
public sealed record NodeInspectorModel
{
    public required string ComponentType { get; init; }
    public IReadOnlyList<OptionSectionModel> Sections { get; init; } = [];
    public IReadOnlyList<ResourcePickerPromptModel> ResourcePrompts { get; init; } = [];
}

public sealed record OptionSectionModel
{
    public required string Name { get; init; }
    public IReadOnlyList<OptionEditorModel> Options { get; init; } = [];
}
