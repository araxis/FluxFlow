namespace FluxFlow.Components.Projections;

/// <summary>Stable normal-result kinds emitted by the canonical projection node.</summary>
public static class ProjectionResultKinds
{
    public const string Snapshot = "snapshot";
    public const string FinalSnapshot = "final-snapshot";
    public const string ProjectionFailed = "projection-failed";
}
