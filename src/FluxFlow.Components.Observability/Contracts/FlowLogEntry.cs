namespace FluxFlow.Components.Observability.Contracts;

public sealed record FlowLogEntry
{
    public required DateTimeOffset Timestamp { get; init; }
    public required FlowLogLevel Level { get; init; }
    public required string Category { get; init; }
    public required string Message { get; init; }
    public required string InputType { get; init; }
    public required long Sequence { get; init; }
    public required IReadOnlyDictionary<string, object?> Attributes { get; init; }
}
