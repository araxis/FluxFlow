namespace FluxFlow.Engine;

public sealed record FlowApplicationHostBuildError(
    FlowApplicationHostBuildErrorCode Code,
    string Message,
    Exception? Exception = null,
    string? WorkflowName = null,
    string? NodeName = null,
    string? PortName = null);
