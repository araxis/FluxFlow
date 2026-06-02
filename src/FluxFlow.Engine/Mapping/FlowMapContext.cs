namespace FluxFlow.Engine.Mapping;

/// <summary>
/// Per-message mapping context passed to mapper functions and expression engines.
/// The input remains the first-class Map argument; Variables carries named values
/// used by host-provided expression engines.
/// </summary>
public sealed record FlowMapContext
{
    public IReadOnlyDictionary<string, object?> Variables { get; init; } = new Dictionary<string, object?>();
}
