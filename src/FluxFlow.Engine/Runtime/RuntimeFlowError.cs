using FluxFlow.Engine.Components;
using FluxFlow.Engine.Definitions;

namespace FluxFlow.Engine.Runtime;

public sealed record RuntimeFlowError
{
    public required NodeAddress NodeAddress { get; init; }
    public required FlowNodeId NodeId { get; init; }
    public NodeType? NodeType { get; init; }
    public int NodePhase { get; init; }
    public required FlowError Error { get; init; }

    public int Code => Error.Code;
    public string Message => Error.Message;
    public Exception? Exception => Error.Exception;
    public DateTimeOffset OccurredAt => Error.OccurredAt;
    public string? Context => Error.Context;
}
