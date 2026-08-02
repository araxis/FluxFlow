namespace FluxFlow.Engine.Internal.Snapshots;

internal sealed record CompositionProviderSnapshotInfo
{
    public required string Name { get; init; }

    public required CompositionProviderBoundary Boundary { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required bool OwnsProvider { get; init; }

    public int? ServiceCount { get; init; }
}
