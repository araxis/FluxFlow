using FluxFlow.Engine.Definitions;

namespace FluxFlow.Engine.Runtime;

public sealed record ApplicationRuntimeBuildError(
    ApplicationRuntimeBuildErrorCode Code,
    string Message,
    string? WorkflowName = null,
    NodeName? NodeName = null,
    PortName? PortName = null);
