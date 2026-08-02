namespace FluxFlow.Composition.Model;

public sealed class WorkflowDefinition
{
    public WorkflowDefinition(
        IEnumerable<KeyValuePair<string, ComponentDefinition>>? components = null)
    {
        Components = DefinitionRules.CopyNamed(
            components,
            nameof(components),
            static (value, parameterName) => DefinitionRules.RequireSegment(
                value,
                parameterName,
                "Component name"));
    }

    public IReadOnlyDictionary<string, ComponentDefinition> Components { get; }
}
