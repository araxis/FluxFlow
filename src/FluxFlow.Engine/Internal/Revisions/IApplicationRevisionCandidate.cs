using FluxFlow.Engine.Internal.Snapshots;

namespace FluxFlow.Engine.Internal.Revisions;

internal interface IApplicationRevisionCandidate : IAsyncDisposable
{
    IReadOnlyList<CompositionProviderSnapshotInfo> ProviderSnapshots { get; }

    ValueTask ActivateAsync(CancellationToken cancellationToken = default);

    ValueTask DrainAsync(CancellationToken cancellationToken = default);
}
