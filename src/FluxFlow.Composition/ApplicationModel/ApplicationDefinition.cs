using System.Text.Json.Serialization;

namespace FluxFlow.Composition.Model;

[JsonConverter(typeof(ApplicationDefinitionJsonConverter))]
public sealed class ApplicationDefinition
{
    public ApplicationDefinition(
        IEnumerable<KeyValuePair<string, ResourceDefinition>>? resources = null,
        IEnumerable<KeyValuePair<string, WorkflowDefinition>>? workflows = null)
    {
        Resources = DefinitionRules.CopyNamed(
            resources,
            nameof(resources),
            DefinitionRules.RequireResourceName);
        Workflows = DefinitionRules.CopyNamed(
            workflows,
            nameof(workflows),
            DefinitionRules.RequireWorkflowName);
    }

    public IReadOnlyDictionary<string, ResourceDefinition> Resources { get; }

    public IReadOnlyDictionary<string, WorkflowDefinition> Workflows { get; }
}
