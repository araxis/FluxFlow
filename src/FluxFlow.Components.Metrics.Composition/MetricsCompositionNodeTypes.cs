using FluxFlow.Composition;

namespace FluxFlow.Components.Metrics.Composition;

public static class MetricsCompositionNodeTypes
{
    public const string Aggregate = "metric.aggregate";
    public const string LegacyAggregate = "metrics.aggregate";

    internal static CompositionComponentTypeDescriptor AggregateDescriptor { get; } =
        new(Aggregate, [LegacyAggregate]);
}
