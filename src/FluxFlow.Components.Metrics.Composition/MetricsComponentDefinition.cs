namespace FluxFlow.Components.Metrics.Composition;

public static partial class MetricsComponentDefinition
{
    public static class Options
    {
        public const string RateWindowSeconds = "rateWindowSeconds";
        public const string BoundedCapacity = "boundedCapacity";
        public const string MaxGroups = "maxGroups";
        public const string EmitEverySample = "emitEverySample";
        public const string TrackLatest = "trackLatest";
        public const string TrackMinMax = "trackMinMax";
        public const string TrackSize = "trackSize";
        public const string GroupByTag = "groupByTag";
        public const string TreatMissingValueAsZero = "treatMissingValueAsZero";
    }

    public static class Types { public const string Aggregate = "metric.aggregate"; }
    public static class Ports { public const string Input = "Input"; public const string Output = "Output"; }
    public static class Resources { public const string Clock = "clock"; }
}
