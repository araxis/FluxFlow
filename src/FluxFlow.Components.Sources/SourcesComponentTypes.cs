using FluxFlow.Engine.Definitions;

namespace FluxFlow.Components.Sources;

public static class SourcesComponentTypes
{
    public static readonly NodeType Generated = new("source.generated");
    public static readonly NodeType Sequence = new("source.sequence");
}
