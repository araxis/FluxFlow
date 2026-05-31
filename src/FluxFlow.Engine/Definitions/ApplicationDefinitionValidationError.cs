namespace FluxFlow.Engine.Definitions;

public sealed record ApplicationDefinitionValidationError(
    ApplicationDefinitionValidationErrorCode Code,
    string Message,
    string? WorkflowName = null,
    string? NodeName = null,
    string? PortName = null);
