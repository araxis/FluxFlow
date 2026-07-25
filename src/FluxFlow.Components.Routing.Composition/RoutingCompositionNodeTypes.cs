using FluxFlow.Composition;

namespace FluxFlow.Components.Routing.Composition;

public static class RoutingCompositionNodeTypes
{
    public const string Window = "flow.window";

    public const string Correlation = "flow.correlate";
    public const string LegacyCorrelation = "flow.correlation";

    public const string Join = "flow.join";

    internal static CompositionComponentTypeDescriptor CorrelationDescriptor { get; } =
        new(Correlation, [LegacyCorrelation]);
}
