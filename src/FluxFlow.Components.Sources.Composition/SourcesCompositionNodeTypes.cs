using FluxFlow.Composition;

namespace FluxFlow.Components.Sources.Composition;

public static class SourcesCompositionNodeTypes
{
    public const string Generated = "source.items";
    public const string LegacyGenerated = "source.generated";

    public const string Sequence = "source.sequence";

    internal static CompositionComponentTypeDescriptor GeneratedDescriptor { get; } =
        new(Generated, [LegacyGenerated]);
}
