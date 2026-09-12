using FluxFlow.Json.Links;
using System.Text.Json;
using FluxFlow.Composition.Links;
using FluxFlow.Json.Model;

namespace FluxFlow.Composition.Authoring;

public static class ApplicationSetupExportExtensions
{
    public static ApplicationDefinition ToDefinition(this ApplicationSetup setup)
    {
        ArgumentNullException.ThrowIfNull(setup);
        if (setup.Links.Any(link => link.CodeCondition is not null))
            throw new NotSupportedException("Native predicates cannot be exported. Use a named expression for a portable condition.");
        if (setup.ComponentDescriptors.Count != 0 || setup.ResourceContracts.Count != 0)
            throw new NotSupportedException("Runtime activation contracts cannot be exported as portable constructor bindings.");

        return Project(setup.Definition, setup.Links);
    }

    internal static ApplicationDefinition Project(ApplicationDefinition definition, IEnumerable<ApplicationLinkDefinition> links)
    {
        var workflows = definition.Workflows.ToDictionary(workflow => workflow.Key,
            workflow => workflow.Value.Components.ToDictionary(component => component.Key,
                component => component.Value.Properties.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal),
                StringComparer.Ordinal), StringComparer.Ordinal);
        var projections = links.Select(link => new ApplicationLinkDeclarationProjection(
            link.Source, link.Target, link.ConditionExpression, link.DeclarationSide, delivery: link.Delivery,
            isFallback: link.IsFallback));
        foreach (var group in projections.GroupBy(link => link.DeclaredPort))
        {
            var parts = group.Key.Segments;
            var properties = workflows[parts[0]][parts[1]];
            var declarations = new List<ParsedApplicationLinkDeclaration>();
            if (properties.TryGetValue(parts[2], out var existing))
            {
                var parsed = ApplicationLinkDeclarationParser.Parse(existing, group.Key.Value);
                if (parsed.Errors.Count != 0)
                    throw new JsonException($"Existing link declarations at '{group.Key}' are invalid.");
                declarations.AddRange(parsed.Declarations);
            }
            declarations.AddRange(group.Select(link => new ParsedApplicationLinkDeclaration(link.PortReference,
                link.ConditionExpression, link.Parameters, link.Delivery, link.IsFallback)));
            properties[parts[2]] = ApplicationLinkDeclarationParser.Serialize(declarations);
        }

        return new ApplicationDefinition(definition.Resources, definition.Workflows.Select(workflow =>
            new KeyValuePair<string, WorkflowDefinition>(workflow.Key, new WorkflowDefinition(
                workflow.Value.Components.Select(component => new KeyValuePair<string, ComponentDefinition>(component.Key,
                    new ComponentDefinition(component.Value.Type, workflows[workflow.Key][component.Key]))),
                workflow.Value.ExpressionEngine))), definition.ExpressionEngine);
    }
}
