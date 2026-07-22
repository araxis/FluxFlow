using FluxFlow.Composition;

namespace FluxFlow.Components.Routing.Composition;

public static class RoutingCompositionNodeTypes
{
    public const string Switch = "flow.switch";

    public const string Fork = "flow.fork";

    public const string Merge = "flow.merge";

    public const string Window = "flow.window";

    public const string Correlation = "flow.correlate";
    public const string LegacyCorrelation = "flow.correlation";

    public const string Join = "flow.join";

    internal static CompositionComponentTypeDescriptor CorrelationDescriptor { get; } =
        new(Correlation, [LegacyCorrelation]);
}
