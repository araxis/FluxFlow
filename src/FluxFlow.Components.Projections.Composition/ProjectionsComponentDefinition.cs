namespace FluxFlow.Components.Projections.Composition;

public static partial class ProjectionsComponentDefinition
{
    public static class Options
    {
        public const string Name = "name";
        public const string Filter = "filter";
        public const string RateWindowSeconds = "rateWindowSeconds";
        public const string EmitEveryMatch = "emitEveryMatch";
        public const string EmitFinalSnapshot = "emitFinalSnapshot";
        public const string MaxPreviewChars = "maxPreviewChars";
        public const string BoundedCapacity = "boundedCapacity";
    }

    public static class Types { public const string EventProjection = "event.project"; }
    public static class Ports { public const string Input = "Input"; public const string Output = "Output"; public const string Events = "Events"; }
    public static class Resources { public const string Clock = "clock"; }
}
