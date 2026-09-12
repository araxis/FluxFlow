using FluxFlow.Nodes;

namespace FluxFlow.Components.Assertions.Composition;

public static class AssertionsComponentDefinition
{
    public static class Types
    {
        public const string Assertion = "data.assert";
    }

    public static class Options
    {
        public const string Expression = "expression";
        public const string ExpressionId = "expressionId";
        public const string ExpressionName = "expressionName";
        public const string InputType = "inputType";
        public const string BoundedCapacity = "boundedCapacity";
        public const string Description = "description";
        public const string FailureMessage = "failureMessage";
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
