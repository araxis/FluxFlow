using FluxFlow.Composition.Hosting.Snapshots;

namespace FluxFlow.Composition.Hosting.Revisions;

public interface IApplicationRevisionCandidate : IAsyncDisposable
{
    IReadOnlyList<CompositionProviderSnapshotInfo> ProviderSnapshots { get; }

    ValueTask ActivateAsync(CancellationToken cancellationToken = default);

    ValueTask DrainAsync(CancellationToken cancellationToken = default);
}
