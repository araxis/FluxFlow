using FluxFlow.Engine.Definitions;

namespace FluxFlow.Engine.Runtime;

public sealed record WorkflowStateChanged(
    WorkflowName WorkflowName,
    WorkflowState Previous,
    WorkflowState Current,
    Exception? Exception = null)
{
    public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
}
