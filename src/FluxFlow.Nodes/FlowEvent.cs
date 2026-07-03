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
    private IReadOnlyDictionary<string, object?> _attributes =
        new Dictionary<string, object?>(StringComparer.Ordinal);

    public DateTimeOffset Timestamp { get; init; }

    /// <summary>The correlation id of the message this event relates to, if any.</summary>
    public CorrelationId? CorrelationId { get; init; }

    public required string Name { get; init; }

    public FlowEventLevel Level { get; init; } = FlowEventLevel.Information;

    public string? Message { get; init; }

    public IReadOnlyDictionary<string, object?> Attributes
    {
        get => _attributes;
        init => _attributes = CopyAttributes(value);
    }

    private static IReadOnlyDictionary<string, object?> CopyAttributes(
        IReadOnlyDictionary<string, object?>? attributes)
        => attributes is null
            ? new Dictionary<string, object?>(StringComparer.Ordinal)
            : new Dictionary<string, object?>(attributes, StringComparer.Ordinal);
}
