namespace FluxFlow.DesignerHost;

/// <summary>
/// Host-local view of one component for a palette list: what to label it, how to
/// group it, and which fixed ports a dropped node starts with. Display values fall
/// back conservatively (type string for the name, "General" for the category) so a
/// palette never renders an empty label.
/// </summary>
public sealed record PaletteItemModel
{
    public required string ComponentType { get; init; }
    public required string DisplayName { get; init; }
    public required string Category { get; init; }
    public string? Summary { get; init; }
    public string? IconKey { get; init; }
    public string? PreferredNodeName { get; init; }
    public IReadOnlyList<PortModel> Inputs { get; init; } = [];
    public IReadOnlyList<PortModel> Outputs { get; init; } = [];
}
