namespace FluxFlow.Json.Model;

public sealed class WorkflowDefinition
{
    internal IReadOnlyList<string>? PropertyOrder { get; init; }

    public WorkflowDefinition(
        IEnumerable<KeyValuePair<string, ComponentDefinition>>? components = null,
        string? expressionEngine = null,
        IEnumerable<KeyValuePair<string, ResourceDefinition>>? resources = null)
    {
        ExpressionEngine = expressionEngine is null ? null : DefinitionRules.RequireType(expressionEngine, nameof(expressionEngine));
        Resources = DefinitionRules.CopyNamed(resources, nameof(resources), DefinitionRules.RequireResourceName);
        Components = DefinitionRules.CopyNamed(
            components,
            nameof(components),
            static (value, parameterName) => DefinitionRules.RequireSegment(
                value,
                parameterName,
                "Component name"));
        if (Components.Keys.Any(name => name.Equals("ExpressionEngine", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Resources", StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Component names 'ExpressionEngine' and 'Resources' are reserved for workflow configuration.", nameof(components));
    }

    public string? ExpressionEngine { get; }

    public IReadOnlyDictionary<string, ResourceDefinition> Resources { get; }

    public IReadOnlyDictionary<string, ComponentDefinition> Components { get; }
}
