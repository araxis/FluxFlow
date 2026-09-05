namespace FluxFlow.Components.Observability;

public static class ObservabilityErrorCodeNames
{
    public const string MissingInput = "observability.missing_input";
    public const string CounterPredicateFailed = "observability.counter_predicate_failed";
    public const string LoggerAttributeSelectorFailed = "observability.logger_attribute_selector_failed";
    public const string LoggerFailed = "observability.logger_failed";
    public const string MetricsSizeSelectorFailed = "observability.metrics_size_selector_failed";
    public const string MetricsFailed = "observability.metrics_failed";
}
