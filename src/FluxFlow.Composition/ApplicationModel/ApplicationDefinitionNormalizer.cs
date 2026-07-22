namespace FluxFlow.Composition.Model;

public sealed class ApplicationDefinitionNormalizer
{
    private readonly CompositionNodeRegistry _components;

    public ApplicationDefinitionNormalizer(CompositionNodeRegistry components)
    {
        _components = components ?? throw new ArgumentNullException(nameof(components));
    }

    public ApplicationDefinitionNormalizationResult Normalize(ApplicationDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var diagnostics = new List<ApplicationDefinitionNormalizationDiagnostic>();
        var resources = definition.Resources
            .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new KeyValuePair<string, ResourceDefinition>(
                pair.Key,
                NormalizeResource(pair.Value, $"Resources.{pair.Key}", diagnostics)))
            .ToArray();
        var workflows = definition.Workflows
            .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new KeyValuePair<string, WorkflowDefinition>(
                pair.Key,
                NormalizeWorkflow(pair.Value, pair.Key, diagnostics)))
            .ToArray();

        return new ApplicationDefinitionNormalizationResult
        {
            Definition = diagnostics.Count == 0
                ? definition
                : new ApplicationDefinition(resources, workflows),
            Diagnostics = diagnostics.ToArray()
        };
    }

    private WorkflowDefinition NormalizeWorkflow(
        WorkflowDefinition workflow,
        string workflowName,
        ICollection<ApplicationDefinitionNormalizationDiagnostic> diagnostics)
    {
        var changed = false;
        var components = workflow.Components
            .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .Select(pair =>
            {
                var component = pair.Value;
                if (!_components.TryResolveType(component.Type, out var canonicalType) ||
                    string.Equals(component.Type, canonicalType, StringComparison.Ordinal))
                {
                    return pair;
                }

                changed = true;
                var path = $"Workflows.{workflowName}.{pair.Key}.Type";
                diagnostics.Add(new ApplicationDefinitionNormalizationDiagnostic
                {
                    Code = "definition.component_type_alias",
                    Kind = ApplicationDefinitionNormalizationDiagnosticKind.ComponentTypeAlias,
                    Path = path,
                    PreviousType = component.Type,
                    CanonicalType = canonicalType,
                    Message = $"Component type '{component.Type}' at '{path}' was normalized to '{canonicalType}'."
                });
                return new KeyValuePair<string, ComponentDefinition>(
                    pair.Key,
                    new ComponentDefinition(canonicalType, component.Properties));
            })
            .ToArray();

        return changed ? new WorkflowDefinition(components) : workflow;
    }

    private ResourceDefinition NormalizeResource(
        ResourceDefinition resource,
        string path,
        ICollection<ApplicationDefinitionNormalizationDiagnostic> diagnostics)
    {
        if (resource is ResourceGroupDefinition group)
        {
            var changed = false;
            var resources = group.Resources
                .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                .Select(pair =>
                {
                    var normalized = NormalizeResource(pair.Value, $"{path}.{pair.Key}", diagnostics);
                    changed |= !ReferenceEquals(normalized, pair.Value);
                    return new KeyValuePair<string, ResourceDefinition>(pair.Key, normalized);
                })
                .ToArray();
            return changed ? new ResourceGroupDefinition(resources) : group;
        }

        var instance = (ResourceInstanceDefinition)resource;
        if (!_components.TryResolveResourceType(instance.Type, out var canonicalType))
            return instance;

        var typePath = $"{path}.Type";
        diagnostics.Add(new ApplicationDefinitionNormalizationDiagnostic
        {
            Code = "definition.resource_type_alias",
            Kind = ApplicationDefinitionNormalizationDiagnosticKind.ResourceTypeAlias,
            Path = typePath,
            PreviousType = instance.Type,
            CanonicalType = canonicalType,
            Message = $"Resource type '{instance.Type}' at '{typePath}' was normalized to '{canonicalType}'."
        });
        return new ResourceInstanceDefinition(canonicalType, instance.Properties);
    }

}
