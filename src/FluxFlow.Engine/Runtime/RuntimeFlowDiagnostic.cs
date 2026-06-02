using FluxFlow.Engine.Components;
using FluxFlow.Engine.Definitions;

namespace FluxFlow.Engine.Runtime;

public sealed record RuntimeFlowDiagnostic
{
    public required NodeAddress NodeAddress { get; init; }
    public required FlowNodeId NodeId { get; init; }
    public NodeType? NodeType { get; init; }
    public int NodePhase { get; init; }
    public required FlowDiagnostic Diagnostic { get; init; }

    public DateTimeOffset Timestamp => Diagnostic.Timestamp;
    public string Name => Diagnostic.Name;
    public FlowDiagnosticLevel Level => Diagnostic.Level;
    public string? Message => Diagnostic.Message;
    public Exception? Exception => Diagnostic.Exception;
    public IReadOnlyDictionary<string, object?> Attributes => Diagnostic.Attributes;
}
