namespace FluxFlow.Components.Observability;

public static class ObservabilityResultKinds
{
    public const string CounterSnapshot = "counter-snapshot";
    public const string CounterRejected = "counter-rejected";
    public const string CounterFailed = "counter-failed";
    public const string LogEntry = "log-entry";
    public const string LogEntryPartial = "log-entry-partial";
    public const string LoggerFailed = "logger-failed";
    public const string MetricSnapshot = "metric-snapshot";
    public const string MetricSnapshotPartial = "metric-snapshot-partial";
    public const string MetricsFailed = "metrics-failed";
}
