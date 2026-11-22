namespace FluxFlow.Authoring;

/// <summary>
/// Presentation and data-shape hints for one fixed component port.
/// </summary>
public sealed record PortModel
{
    public required string Name { get; init; }
    public required PortKind Kind { get; init; }
    public string? DisplayName { get; init; }
    /// <summary>Studio color role: input, output, events or error. Omitted uses the port direction.</summary>
    public string? ColorRole { get; init; }
    public string? Group { get; init; }
    public int Order { get; init; }
    public string? Summary { get; init; }
    /// <summary>Presentation-only reason this port is inactive for the current configuration. Its contract and saved links remain available.</summary>
    public OptionVisibility? ActiveWhen { get; init; }
    public string? InactiveHint { get; init; }
    public string? InactiveReason { get; init; }
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
