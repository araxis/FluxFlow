using System.Text.Json.Serialization;
using FluxFlow.Composition.Authoring;
using FluxFlow.Composition.Links;

namespace FluxFlow.Composition.Model;

[JsonConverter(typeof(ApplicationDefinitionJsonConverter))]
public sealed class ApplicationDefinition
{
    public ApplicationDefinition(
        IEnumerable<KeyValuePair<string, ResourceDefinition>>? resources = null,
        IEnumerable<KeyValuePair<string, WorkflowDefinition>>? workflows = null)
        : this(
            resources,
            workflows,
            links: null,
            componentDescriptors: null,
            applicationResourceContracts: null)
    {
    }

    internal ApplicationDefinition(
        IEnumerable<KeyValuePair<string, ResourceDefinition>>? resources,
        IEnumerable<KeyValuePair<string, WorkflowDefinition>>? workflows,
        IEnumerable<ApplicationLinkDefinition>? links,
        IEnumerable<ComponentDescriptor>? componentDescriptors,
        IEnumerable<ApplicationResourceContract>? applicationResourceContracts)
    {
        Resources = DefinitionRules.CopyNamed(
            resources,
            nameof(resources),
            DefinitionRules.RequireResourceName);
        Workflows = DefinitionRules.CopyNamed(
            workflows,
            nameof(workflows),
            DefinitionRules.RequireWorkflowName);
        Links = CopyLinks(links);
        ComponentDescriptors = CopyComponentDescriptors(componentDescriptors);
        ApplicationResourceContracts = CopyResourceContracts(applicationResourceContracts);
    }

    public IReadOnlyDictionary<string, ResourceDefinition> Resources { get; }

    public IReadOnlyDictionary<string, WorkflowDefinition> Workflows { get; }

    public IReadOnlyList<ApplicationLinkDefinition> Links { get; }

    public IReadOnlyList<ComponentDescriptor> ComponentDescriptors { get; }

    public IReadOnlyList<ApplicationResourceContract> ApplicationResourceContracts { get; }

    private static IReadOnlyList<ApplicationLinkDefinition> CopyLinks(
        IEnumerable<ApplicationLinkDefinition>? links)
    {
        if (links is null)
            return Array.Empty<ApplicationLinkDefinition>();

        var values = links.ToArray();
        if (values.Any(static link => link is null))
            throw new ArgumentException("Application links cannot contain null values.", nameof(links));
        return Array.AsReadOnly(values);
    }

    private static IReadOnlyList<ComponentDescriptor> CopyComponentDescriptors(
        IEnumerable<ComponentDescriptor>? descriptors)
    {
        if (descriptors is null)
            return Array.Empty<ComponentDescriptor>();

        return new ComponentCatalog(descriptors).Descriptors;
    }

    private static IReadOnlyList<ApplicationResourceContract> CopyResourceContracts(
        IEnumerable<ApplicationResourceContract>? contracts)
    {
        if (contracts is null)
            return Array.Empty<ApplicationResourceContract>();

        var values = contracts.ToArray();
        if (values.Any(static contract => contract is null))
        {
            throw new ArgumentException(
                "Application resource contracts cannot contain null values.",
                nameof(contracts));
        }

        var duplicate = values
            .GroupBy(static contract => contract.Type, StringComparer.Ordinal)
            .FirstOrDefault(static group => group.Skip(1).Any());
        if (duplicate is not null)
        {
            throw new ArgumentException(
                $"Application resource type '{duplicate.Key}' has more than one contract.",
                nameof(contracts));
        }

        return Array.AsReadOnly(values);
    }
}
