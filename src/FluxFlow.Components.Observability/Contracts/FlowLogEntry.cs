using FluxFlow.Data;

namespace FluxFlow.Components.Observability.Contracts;

public sealed record FlowLogEntry
{
    public required DateTimeOffset Timestamp { get; init; }

    public required FlowLogLevel Level { get; init; }

    public required string Category { get; init; }

    public required string Message { get; init; }

    public required long Sequence { get; init; }

    public required FlowValue Input { get; init; }

    public required FlowValue Attributes { get; init; }
}
