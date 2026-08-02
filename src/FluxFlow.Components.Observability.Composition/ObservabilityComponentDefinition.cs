namespace FluxFlow.Components.Observability.Composition;

public static partial class ObservabilityComponentDefinition
{
    public static class Options
    {
        public const string Name = "name";
        public const string Predicate = "predicate";
        public const string ExpressionId = "expressionId";
        public const string ExpressionName = "expressionName";
        public const string BoundedCapacity = "boundedCapacity";
        public const string Level = "level";
        public const string Category = "category";
        public const string MessageTemplate = "messageTemplate";
        public const string AttributeSelectors = "attributeSelectors";
    }

    public static class Types { public const string Counter = "metric.count"; public const string Logger = "log.write"; public const string Metrics = "metric.measure"; }
    public static class Ports { public const string Input = "Input"; public const string Output = "Output"; }
    public static class Resources
    {
        public const string Clock = "clock";
        public const string Engine = "engine";
        public const string ContextFactory = "contextFactory";
        public const string SizeSelector = "sizeSelector";
        internal const string AttributeSelectorPrefix = "attribute:";

        public static string AttributeSelector(string name)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            return AttributeSelectorPrefix + name.Trim();
        }
    }
}
