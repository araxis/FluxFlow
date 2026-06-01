namespace FluxFlow.Components.Observability.Diagnostics;

public static class ObservabilityDiagnosticNames
{
    public const string CounterIncremented = "flow.counter.incremented";
    public const string CounterRejected = "flow.counter.rejected";
    public const string CounterFailed = "flow.counter.failed";
    public const string LoggerEmitted = "flow.logger.emitted";
    public const string LoggerFailed = "flow.logger.failed";
    public const string MetricsObserved = "flow.metrics.observed";
    public const string MetricsFailed = "flow.metrics.failed";
}
