namespace FluxFlow.Engine.Internal.Revisions;

internal enum ApplicationRevisionPhase
{
    Proposed = 1,
    Accepted = 2,
    Rejected = 3,
    Activated = 4,
    Draining = 5,
    Disposed = 6
}
