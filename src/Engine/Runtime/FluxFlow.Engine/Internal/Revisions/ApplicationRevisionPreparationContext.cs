using FluxFlow.Engine.Internal.Revisions;

namespace FluxFlow.Engine.Internal.Revisions;

internal sealed class ApplicationRevisionPreparationContext
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
