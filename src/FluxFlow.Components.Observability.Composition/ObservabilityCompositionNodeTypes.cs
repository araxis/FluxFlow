using FluxFlow.Composition;

namespace FluxFlow.Components.Observability.Composition;

public static class ObservabilityCompositionNodeTypes
{
    public const string Counter = "metric.count";
    public const string LegacyCounter = "flow.counter";

    public const string Logger = "log.write";
    public const string LegacyLogger = "flow.logger";

    public const string Metrics = "metric.measure";
    public const string LegacyMetrics = "flow.metrics";

    internal static CompositionComponentTypeDescriptor CounterDescriptor { get; } =
        new(Counter, [LegacyCounter]);

    internal static CompositionComponentTypeDescriptor LoggerDescriptor { get; } =
        new(Logger, [LegacyLogger]);

    internal static CompositionComponentTypeDescriptor MetricsDescriptor { get; } =
        new(Metrics, [LegacyMetrics]);
}
