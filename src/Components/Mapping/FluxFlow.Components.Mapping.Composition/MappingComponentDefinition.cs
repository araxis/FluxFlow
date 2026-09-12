using FluxFlow.Nodes;

namespace FluxFlow.Components.Mapping.Composition;

public static partial class MappingComponentDefinition
{
    public static class Options
    {
        public const string Expression = "expression";
        public const string ExpressionId = "expressionId";
        public const string ExpressionName = "expressionName";
        public const string InputType = "inputType";
        public const string OutputType = "outputType";
        public const string BoundedCapacity = "boundedCapacity";
    }

    public static class Types
    {
        public const string Mapper = "data.map";
    }

    public static class Ports
    {
        public const string Input = FlowPorts.InputName;
        public const string Output = FlowPorts.OutputName;
        public const string Events = FlowPorts.EventsName;
    }

    public static class Resources
    {
        public const string Engine = "engine";

        public const string ContextFactory = "contextFactory";

        public const string Clock = "clock";
    }
}
