namespace FluxFlow.Components.Metrics;

/// <summary>Stable normal-result kinds emitted by the canonical metrics node.</summary>
public static class MetricsResultKinds
{
    public const string Snapshot = "snapshot";
    public const string FinalSnapshot = "final-snapshot";
    public const string GroupLimitReached = "group-limit-reached";
    public const string AggregateFailed = "aggregate-failed";
}
