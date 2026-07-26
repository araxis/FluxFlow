namespace FluxFlow.Composition.Hosting.Revisions;

public sealed record ApplicationWorkflowRevisionChange
{
    public required string Workflow { get; init; }

    public required ApplicationRevisionChangeKind Kind { get; init; }
}
