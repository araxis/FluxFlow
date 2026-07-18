namespace FluxFlow.Composition.Hosting.Revisions;

public enum ApplicationRevisionUpdateStatus
{
    Unchanged = 1,
    Rejected = 2,
    Activated = 3,
    ActivatedWithFailures = 4
}
