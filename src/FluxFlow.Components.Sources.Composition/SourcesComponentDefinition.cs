namespace FluxFlow.Components.Sources.Composition;

public static partial class SourcesComponentDefinition
{
    public static class Options
    {
        public const string Name = "name";
        public const string Items = "items";
        public const string Loop = "loop";
        public const string MaxItems = "maxItems";
        public const string InitialDelayMilliseconds = "initialDelayMilliseconds";
        public const string IntervalMilliseconds = "intervalMilliseconds";
        public const string BoundedCapacity = "boundedCapacity";
        public const string Start = "start";
        public const string Step = "step";
        public const string Count = "count";
    }

    public static class Types { public const string Generated = "source.items"; public const string Sequence = "source.sequence"; }
    public static class Ports { public const string Output = "Output"; }
    public static class Resources { public const string Clock = "clock"; }
}
