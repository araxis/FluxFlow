namespace FluxFlow.Components.Routing.Composition;

public static partial class RoutingComponentDefinition
{
    public static class Options
    {
        public const string InputType = "inputType";
        public const string MaxItems = "maxItems";
        public const string TimeMilliseconds = "timeMilliseconds";
        public const string EmitPartialOnCompletion = "emitPartialOnCompletion";
        public const string BoundedCapacity = "boundedCapacity";
        public const string Engine = "engine";
        public const string KeyExpression = "keyExpression";
        public const string SideExpression = "sideExpression";
        public const string ExpressionId = "expressionId";
        public const string ExpressionName = "expressionName";
        public const string RequestSide = "requestSide";
        public const string ResponseSide = "responseSide";
        public const string CaseSensitive = "caseSensitive";
        public const string TimeoutMilliseconds = "timeoutMilliseconds";
        public const string MaxPending = "maxPending";
        public const string LeftKeyExpression = "leftKeyExpression";
        public const string RightKeyExpression = "rightKeyExpression";
        public const string LeftInputType = "leftInputType";
        public const string RightInputType = "rightInputType";
    }

    public static class Types { public const string Window = "flow.window"; public const string Correlation = "flow.correlate"; public const string Join = "flow.join"; }
    public static class Ports { public const string Input = "Input"; public const string Output = "Output"; public const string Left = "Left"; public const string Right = "Right"; public const string Events = "Events"; }
    public static class Resources { public const string Clock = "clock"; public const string KeySelector = "keySelector"; public const string SideSelector = "sideSelector"; public const string LeftKeySelector = "leftKeySelector"; public const string RightKeySelector = "rightKeySelector"; }
}
