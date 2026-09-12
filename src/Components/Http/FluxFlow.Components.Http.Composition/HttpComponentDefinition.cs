using FluxFlow.Nodes;

namespace FluxFlow.Components.Http.Composition;

public static partial class HttpComponentDefinition
{
    public static class Options
    {
        public const string BoundedCapacity = "boundedCapacity";
        public const string MaxResponseBodyBytes = "maxResponseBodyBytes";
        public const string TreatNonSuccessStatusAsError = "treatNonSuccessStatusAsError";
        public const string MaxDegreeOfParallelism = "maxDegreeOfParallelism";
        public const string DefaultTimeoutMilliseconds = "defaultTimeoutMilliseconds";
    }

    public static class Types
    {
        public const string Client = "http.request";
    }

    public static class Ports
    {
        public const string Input = FlowPorts.InputName;
        public const string Output = FlowPorts.OutputName;
        public const string Events = FlowPorts.EventsName;
    }

    public static class Resources
    {
        public const string Client = "client";
        public const string Clock = "clock";
    }
}
