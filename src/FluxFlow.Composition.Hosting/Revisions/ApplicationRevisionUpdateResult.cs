using FluxFlow.Composition.Revisions;

namespace FluxFlow.Composition.Hosting.Revisions;

public sealed class ApplicationRevisionUpdateResult
{
    internal ApplicationRevisionUpdateResult(
        long sequence,
        string revisionId,
        ApplicationRevisionUpdateStatus status,
        ApplicationRevisionPlan? plan,
        ApplicationRevisionSnapshot? snapshot,
        IEnumerable<ApplicationRevisionFailure> failures)
    {
        Sequence = sequence;
        RevisionId = revisionId;
        Status = status;
        Plan = plan;
        Snapshot = snapshot;
        Failures = failures.ToArray();
    }

    public long Sequence { get; }

    public string RevisionId { get; }

    public ApplicationRevisionUpdateStatus Status { get; }

    public ApplicationRevisionPlan? Plan { get; }

    public ApplicationRevisionSnapshot? Snapshot { get; }

    public IReadOnlyList<ApplicationRevisionFailure> Failures { get; }

    public bool IsActivated => Status is ApplicationRevisionUpdateStatus.Activated or
        ApplicationRevisionUpdateStatus.ActivatedWithFailures;
}
