namespace FluxFlow.DesignerHost;

/// <summary>
/// Host-local view of one fixed component port. Values are plain strings copied
/// from package metadata so renderer code never depends on Designer contract types.
/// </summary>
public sealed record PortModel
{
    public required string Name { get; init; }
    public required PortKind Kind { get; init; }
    public string? DisplayName { get; init; }
    public string? Group { get; init; }
    public int Order { get; init; }
    public string? Summary { get; init; }
    public string? ValueType { get; init; }
    public bool IsPrimary { get; init; }
    public InputShapeModel? InputShape { get; init; }
}

/// <summary>Serializable structural hints for a destination input.</summary>
public sealed record InputShapeModel
{
    public required string Description { get; init; }
    public IReadOnlyList<string> AcceptedKinds { get; init; } = [];
    public IReadOnlyList<string> RequiredProperties { get; init; } = [];
}

public enum PortKind
{
    Input,
    SignalInput,
    Output
}
