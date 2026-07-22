using FluxFlow.Composition;

namespace FluxFlow.Components.Expectations.Composition;

public static class ExpectationsCompositionNodeTypes
{
    public const string EventExpectation = "event.expect";
    public const string LegacyEventExpectation = "event.expectation";

    internal static CompositionComponentTypeDescriptor EventExpectationDescriptor { get; } =
        new(EventExpectation, [LegacyEventExpectation]);
}
