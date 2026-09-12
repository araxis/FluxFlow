using System.Text.Json.Serialization;

namespace FluxFlow.Json.Model;

/// <summary>Portable application data. Activation contracts and native links belong to ApplicationSetup.</summary>
[JsonConverter(typeof(ApplicationDefinitionJsonConverter))]
public sealed class ApplicationDefinition
{
    public ApplicationDefinition(
        IEnumerable<KeyValuePair<string, ResourceDefinition>>? resources = null,
        IEnumerable<KeyValuePair<string, WorkflowDefinition>>? workflows = null,
        string? expressionEngine = null)
    {
        ExpressionEngine = expressionEngine is null ? null : DefinitionRules.RequireType(expressionEngine, nameof(expressionEngine));
        Resources = DefinitionRules.CopyNamed(resources, nameof(resources), DefinitionRules.RequireResourceName);
        Workflows = DefinitionRules.CopyNamed(workflows, nameof(workflows), DefinitionRules.RequireWorkflowName);
    }

    public IReadOnlyDictionary<string, ResourceDefinition> Resources { get; }

    public string? ExpressionEngine { get; }

    public IReadOnlyDictionary<string, WorkflowDefinition> Workflows { get; }
}
