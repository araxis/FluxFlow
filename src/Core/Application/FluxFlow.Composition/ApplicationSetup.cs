using FluxFlow.Nodes;
using FluxFlow.Composition.Definition;
using FluxFlow.Composition.Links;
using FluxFlow.Json.Model;

namespace FluxFlow.Composition;

/// <summary>Definition result: portable data and explicit in-process activation and link contracts.</summary>
public sealed class ApplicationSetup
{
    public ApplicationSetup(
        ApplicationDefinition definition,
        IEnumerable<ApplicationLinkDefinition>? links = null,
        IEnumerable<ApplicationResourceContract>? resourceContracts = null,
        IEnumerable<KeyValuePair<string, Func<ComponentActivationContext, ValueTask<IFlowNode>>>>? componentFactories = null)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        Links = CopyLinks(links);
        ResourceContracts = CopyResourceContracts(resourceContracts);
        var factories = new Dictionary<string, Func<ComponentActivationContext, ValueTask<IFlowNode>>>(StringComparer.Ordinal);
        foreach (var (type, factory) in componentFactories ?? [])
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(type);
            ArgumentNullException.ThrowIfNull(factory);
            factories.Add(type.Trim(), factory);
        }
        ComponentFactories = new System.Collections.ObjectModel.ReadOnlyDictionary<string, Func<ComponentActivationContext, ValueTask<IFlowNode>>>(factories);
    }

    public IReadOnlyDictionary<string, Func<ComponentActivationContext, ValueTask<IFlowNode>>> ComponentFactories { get; }

    public ApplicationDefinition Definition { get; }

    public IReadOnlyList<ApplicationLinkDefinition> Links { get; }

    public IReadOnlyList<ApplicationResourceContract> ResourceContracts { get; }

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
