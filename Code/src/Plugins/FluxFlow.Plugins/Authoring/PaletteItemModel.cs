namespace FluxFlow.Authoring;

/// <summary>
/// Shared presentation for the palette, canvas and Inspector, supplied by a component description.
/// </summary>
public sealed record PaletteItemModel
{
    public required string ComponentType { get; init; }
    public required string DisplayName { get; init; }
    public required string Category { get; init; }
    public string? Summary { get; init; }
    public string? Icon { get; init; }
    public string? DefaultColor { get; init; }
    public string? PreferredNodeName { get; init; }
    public IReadOnlyList<PortModel> Inputs { get; init; } = [];
    public IReadOnlyList<PortModel> SignalInputs { get; init; } = [];
    public IReadOnlyList<PortModel> Outputs { get; init; } = [];
}
