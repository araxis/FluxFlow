namespace FluxFlow.Json.Model;

public sealed class WorkflowDefinition
{
    public WorkflowDefinition(
        IEnumerable<KeyValuePair<string, ComponentDefinition>>? components = null,
        string? expressionEngine = null)
    {
        ExpressionEngine = expressionEngine is null ? null : DefinitionRules.RequireType(expressionEngine, nameof(expressionEngine));
        Components = DefinitionRules.CopyNamed(
            components,
            nameof(components),
            static (value, parameterName) => DefinitionRules.RequireSegment(
                value,
                parameterName,
                "Component name"));
        if (Components.ContainsKey("ExpressionEngine"))
            throw new ArgumentException("Component name 'ExpressionEngine' is reserved for workflow configuration.", nameof(components));
    }

    public string? ExpressionEngine { get; }

    public IReadOnlyDictionary<string, ComponentDefinition> Components { get; }
}
