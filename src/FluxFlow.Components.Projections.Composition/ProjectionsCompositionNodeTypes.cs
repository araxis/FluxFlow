using FluxFlow.Composition;

namespace FluxFlow.Components.Projections.Composition;

public static class ProjectionsCompositionNodeTypes
{
    public const string EventProjection = "event.project";
    public const string LegacyEventProjection = "event.projection";

    internal static CompositionComponentTypeDescriptor EventProjectionDescriptor { get; } =
        new(EventProjection, [LegacyEventProjection]);
}
