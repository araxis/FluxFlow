using FluxFlow.Composition.Revisions;

namespace FluxFlow.Composition.Hosting.Revisions;

public sealed class ApplicationRevisionPreparationContext
{
    internal ApplicationRevisionPreparationContext(
        long sequence,
        string revisionId,
        ApplicationRevisionPlan plan)
    {
        Sequence = sequence;
        RevisionId = revisionId;
        Plan = plan;
    }

    public long Sequence { get; }

    public string RevisionId { get; }

    public ApplicationRevisionPlan Plan { get; }
}
