using FluxFlow.Composition;

namespace FluxFlow.Components.Assertions.Composition;

public static class AssertionsCompositionNodeTypes
{
    public const string Assert = "data.assert";
    public const string LegacyAssert = "flow.assert";

    internal static CompositionComponentTypeDescriptor AssertDescriptor { get; } =
        new(Assert, [LegacyAssert]);
}
