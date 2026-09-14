using System.Text.Json.Serialization;
using System.Text.Json;
using FluxFlow.Core.Addressing;

namespace FluxFlow.Json.Model;

/// <summary>Portable application data. Activation contracts and native links belong to ApplicationSetup.</summary>
[JsonConverter(typeof(ApplicationDefinitionJsonConverter))]
public sealed class ApplicationDefinition
{
    internal IReadOnlyList<string>? PropertyOrder { get; init; }

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

    /// <summary>Reads a resource's JSON value or a nested property, without activating it.</summary>
    public JsonElement GetResourceValue(string reference, string? currentScope = null)
    {
        var addresses = ApplicationAddress.ResolveResourcePaths(reference, currentScope);
        foreach (var address in addresses)
        {
            var parts = address.Segments;
            var resources = Resources;
            var offset = 1;
            if (parts[0] != "Resources")
            {
                if (!Workflows.TryGetValue(parts[0], out var workflow)) continue;
                resources = workflow.Resources;
                offset = 2;
            }
            if (resources.TryGetValue(parts[offset], out var resource) &&
                ApplicationAddress.TryReadJsonValue(resource.Value, parts.Skip(offset + 1), out var value)) return value;
        }
        throw new KeyNotFoundException($"Resource value '{reference}' was not found; checked {string.Join(", ", addresses)}.");
    }
}
