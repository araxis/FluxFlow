using FluxFlow.Nodes;

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
    public static class Ports
    {
        public const string Input = FlowPorts.InputName;
        public const string Output = FlowPorts.OutputName;
        public const string Events = FlowPorts.EventsName;
    }
    public static class Resources { public const string Clock = "clock"; }
}
