using System.Text.Json;
using FluxFlow.Composition;

namespace FluxFlow.DesignerHost;

/// <summary>
/// Maps between the host graph model and <see cref="CompositionDefinition"/>.
/// The mapping is lossless for definition content (component types, node names,
/// option values, resource references, and port links). Host layout is not part
/// of a definition; <see cref="FromDefinition(CompositionDefinition)"/> leaves
/// layout at its default and the host merges saved layout separately.
/// </summary>
public static class GraphDefinitionMapper
{
    public static CompositionDefinition ToDefinition(GraphModel graph)
        => ToDefinition([graph]);

    public static CompositionDefinition ToDefinition(IEnumerable<GraphModel> graphs)
    {
        ArgumentNullException.ThrowIfNull(graphs);

        var workflows = new Dictionary<string, WorkflowDefinition>(StringComparer.Ordinal);
        foreach (var graph in graphs)
        {
            ArgumentNullException.ThrowIfNull(graph);
            if (!workflows.TryAdd(graph.WorkflowName, ToWorkflow(graph)))
            {
                throw new InvalidOperationException(
                    $"Workflow '{graph.WorkflowName}' is defined by more than one graph.");
            }
        }

        return new CompositionDefinition { Workflows = workflows };
    }

    public static GraphModel FromDefinition(CompositionDefinition definition, string workflowName)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowName);

        var name = workflowName.Trim();
        if (!definition.Workflows.TryGetValue(name, out var workflow))
        {
            throw new InvalidOperationException(
                $"The definition does not contain a workflow named '{name}'.");
        }

        return FromWorkflow(name, workflow);
    }

    public static IReadOnlyList<GraphModel> FromDefinition(CompositionDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        return definition.Workflows
            .Select(pair => FromWorkflow(pair.Key, pair.Value))
            .ToArray();
    }

    private static WorkflowDefinition ToWorkflow(GraphModel graph)
        => new()
        {
            Nodes = graph.Nodes.ToDictionary(
                node => node.Name,
                node => new NodeDefinition
                {
                    Type = node.ComponentType,
                    Configuration = new Dictionary<string, JsonElement>(
                        node.Options, StringComparer.Ordinal),
                    Resources = new Dictionary<string, string>(
                        node.Resources, StringComparer.Ordinal)
                },
                StringComparer.Ordinal),
            Links = graph.Links
                .Select(link => new LinkDefinition
                {
                    From = new PortReference
                    {
                        Workflow = link.FromWorkflow,
                        Node = link.FromNode,
                        Port = link.FromPort
                    },
                    To = new PortReference
                    {
                        Workflow = link.ToWorkflow,
                        Node = link.ToNode,
                        Port = link.ToPort
                    }
                })
                .ToList()
        };

    private static GraphModel FromWorkflow(string workflowName, WorkflowDefinition workflow)
        => new()
        {
            WorkflowName = workflowName,
            Nodes = workflow.Nodes
                .Select(pair => new GraphNodeModel
                {
                    Name = pair.Key,
                    ComponentType = pair.Value.Type,
                    Options = new Dictionary<string, JsonElement>(
                        pair.Value.Configuration, StringComparer.Ordinal),
                    Resources = new Dictionary<string, string>(
                        pair.Value.Resources, StringComparer.Ordinal)
                })
                .ToArray(),
            Links = workflow.Links
                .Select(link => new GraphLinkModel
                {
                    FromWorkflow = link.From.Workflow,
                    FromNode = link.From.Node,
                    FromPort = link.From.Port,
                    ToWorkflow = link.To.Workflow,
                    ToNode = link.To.Node,
                    ToPort = link.To.Port
                })
                .ToArray()
        };
}
