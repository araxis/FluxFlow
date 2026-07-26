using FluxFlow.Composition.Hosting.Snapshots;
using FluxFlow.Composition.Model;
using FluxFlow.Composition.Hosting.Revisions;

namespace FluxFlow.Composition.Hosting.Revisions;

public sealed class ApplicationRevisionSnapshot
{
    internal ApplicationRevisionSnapshot(
        long sequence,
        string revisionId,
        DateTimeOffset activatedAt,
        ApplicationDefinition definition,
        ApplicationRevisionPlan plan,
        IEnumerable<CompositionProviderSnapshotInfo> providerSnapshots)
    {
        Sequence = sequence;
        RevisionId = revisionId;
        ActivatedAt = activatedAt;
        Definition = definition;
        Plan = plan;
        ProviderSnapshots = providerSnapshots
            .Select(static snapshot => snapshot ?? throw new ArgumentException(
                "Provider snapshots cannot contain null entries.",
                nameof(providerSnapshots)))
            .ToArray();
    }

    public long Sequence { get; }

    public string RevisionId { get; }

    public DateTimeOffset ActivatedAt { get; }

    public ApplicationDefinition Definition { get; }

    public ApplicationRevisionPlan Plan { get; }

    public IReadOnlyList<CompositionProviderSnapshotInfo> ProviderSnapshots { get; }
}
