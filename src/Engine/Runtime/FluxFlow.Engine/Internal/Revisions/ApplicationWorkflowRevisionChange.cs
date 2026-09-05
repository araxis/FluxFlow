namespace FluxFlow.Engine.Internal.Revisions;

internal sealed record ApplicationWorkflowRevisionChange
{
    public required string Workflow { get; init; }

    public required ApplicationRevisionChangeKind Kind { get; init; }
}
