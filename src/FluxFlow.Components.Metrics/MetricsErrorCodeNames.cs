namespace FluxFlow.Components.Metrics;

/// <summary>Stable workflow-facing error codes for canonical metrics results.</summary>
public static class MetricsErrorCodeNames
{
    public const string AggregateFailed = "metrics.aggregate_failed";
    public const string InvalidSample = "metrics.invalid_sample";
    public const string GroupLimitReached = "metrics.group_limit_reached";
}
