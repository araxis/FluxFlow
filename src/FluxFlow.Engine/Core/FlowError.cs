using FluxFlow.Engine.Core;

namespace FluxFlow.Engine.Components;

public sealed record FlowError
{
    public required FlowNodeId NodeId { get; init; }
    public required int Code { get; init; }
    public required string Message { get; init; }
    public Exception? Exception { get; init; }
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
    public string? Context { get; init; }
}
