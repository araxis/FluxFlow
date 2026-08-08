namespace FluxFlow.Components.State.Composition;

public static partial class StateComponentDefinition
{
    public static class Options
    {
        public const string KeyExpression = "keyExpression";
        public const string Reducer = "reducer";
        public const string ExpressionId = "expressionId";
        public const string ExpressionName = "expressionName";
        public const string InitialState = "initialState";
        public const string BoundedCapacity = "boundedCapacity";
        public const string MaxKeys = "maxKeys";
    }

    public static class Types { public const string Reducer = "state.reduce"; }
    public static class Ports { public const string Input = "Input"; public const string Output = "Output"; public const string Events = "Events"; }
    public static class Resources { public const string Engine = "engine"; public const string Clock = "clock"; }
}
