namespace FluxFlow.Nodes;

public enum FlowEventLevel
{
    Trace,
    Information,
    Warning,
    Error
}

/// <summary>
/// Uniform observability/event item every node emits on its <c>Events</c> port.
/// </summary>
public sealed record FlowEvent
{
    public DateTimeOffset Timestamp { get; init; }

    public required string Name { get; init; }

    public FlowEventLevel Level { get; init; } = FlowEventLevel.Information;

    public string? Message { get; init; }

    public IReadOnlyDictionary<string, object?> Attributes { get; init; } =
        new Dictionary<string, object?>();
}
