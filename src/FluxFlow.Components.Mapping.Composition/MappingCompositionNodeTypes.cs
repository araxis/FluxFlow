using FluxFlow.Composition;

namespace FluxFlow.Components.Mapping.Composition;

public static class MappingCompositionNodeTypes
{
    public const string Mapper = "data.map";
    public const string LegacyMapper = "flow.mapper";

    internal static CompositionComponentTypeDescriptor MapperDescriptor { get; } =
        new(Mapper, [LegacyMapper]);
}
