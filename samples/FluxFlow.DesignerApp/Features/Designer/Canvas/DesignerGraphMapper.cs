using Blazor.Diagrams;
using Blazor.Diagrams.Core.Anchors;
using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;
using FluxFlow.DesignerHost;
using DiagramPort = Blazor.Diagrams.Core.Models.PortModel;

namespace FluxFlow.DesignerApp.Features.Designer.Canvas;

/// <summary>
/// Maps the canvas diagram to and from the host-model <see cref="GraphModel"/>.
/// Link endpoints are resolved back to named ports (an output port on the source,
/// an input port on the target) so the graph round-trips through
/// <c>GraphDefinitionMapper</c> without the canvas geometry leaking into the
/// definition.
/// </summary>
public static class DesignerGraphMapper
{
    public static GraphModel ToGraph(BlazorDiagram diagram, string workflowName)
    {
        var nodes = diagram.Nodes.OfType<FlowNodeModel>()
            .Select(node => new GraphNodeModel
            {
                Name = node.NodeName,
                ComponentType = node.ComponentType,
                Options = new Dictionary<string, System.Text.Json.JsonElement>(node.Configuration, StringComparer.Ordinal),
                Layout = new GraphLayoutModel { X = node.Position.X, Y = node.Position.Y },
            })
            .ToArray();

        var links = new List<GraphLinkModel>();
        foreach (var link in diagram.Links)
        {
            var from = Endpoint(link.Source);
            var to = Endpoint(link.Target);
            if (from is null || to is null)
            {
                continue; // a half-drawn link with a loose end
            }

            links.Add(new GraphLinkModel
            {
                FromNode = from.Value.Node,
                FromPort = from.Value.Port,
                ToNode = to.Value.Node,
                ToPort = to.Value.Port,
            });
        }

        return new GraphModel { WorkflowName = workflowName, Nodes = nodes, Links = links };
    }

    public static IReadOnlyList<ValidationMessageModel> Load(
        BlazorDiagram diagram,
        GraphModel graph,
        Func<string, PaletteItemModel?> palette)
    {
        var messages = new List<ValidationMessageModel>();
        diagram.Links.Clear();
        diagram.Nodes.Clear();

        var byName = new Dictionary<string, FlowNodeModel>(StringComparer.Ordinal);
        foreach (var node in graph.Nodes)
        {
            var item = palette(node.ComponentType);
            if (item is null)
            {
                messages.Add(Warning(
                    $"Unknown component type '{node.ComponentType}'; node '{node.Name}' was skipped.",
                    node.Name,
                    node.ComponentType));
                continue;
            }

            var model = new FlowNodeModel(node.Name, item, new Point(node.Layout.X, node.Layout.Y));
            foreach (var option in node.Options)
            {
                model.Configuration[option.Key] = option.Value;
            }

            diagram.Nodes.Add(model);
            byName[node.Name] = model;
        }

        foreach (var link in graph.Links)
        {
            if (!byName.TryGetValue(link.FromNode, out var fromNode) ||
                !byName.TryGetValue(link.ToNode, out var toNode))
            {
                messages.Add(Warning(
                    $"Link {link.FromNode}.{link.FromPort} -> {link.ToNode}.{link.ToPort} references a node that was not loaded.",
                    null,
                    null));
                continue;
            }

            var fromPort = PortByName(fromNode, PortAlignment.Right, fromNode.OutputPortNames, link.FromPort);
            var toPort = PortByName(toNode, PortAlignment.Left, toNode.InputPortNames, link.ToPort);
            if (fromPort is null || toPort is null)
            {
                messages.Add(Warning(
                    $"Link {link.FromNode}.{link.FromPort} -> {link.ToNode}.{link.ToPort} references an unknown port.",
                    null,
                    null));
                continue;
            }

            diagram.Links.Add(new LinkModel(fromPort, toPort));
        }

        return messages;
    }

    private static (string Node, string Port)? Endpoint(Anchor anchor)
    {
        if (anchor is not SinglePortAnchor portAnchor || portAnchor.Port.Parent is not FlowNodeModel node)
        {
            return null;
        }

        return (node.NodeName, PortName(node, portAnchor.Port));
    }

    private static string PortName(FlowNodeModel node, DiagramPort port)
    {
        var isOutput = port.Alignment == PortAlignment.Right;
        var names = isOutput ? node.OutputPortNames : node.InputPortNames;
        var index = node.Ports.Where(candidate => candidate.Alignment == port.Alignment).ToList().IndexOf(port);
        if (index >= 0 && index < names.Count)
        {
            return names[index];
        }

        return isOutput ? "Output" : "Input";
    }

    private static DiagramPort? PortByName(
        FlowNodeModel node,
        PortAlignment alignment,
        IReadOnlyList<string> names,
        string portName)
    {
        var ports = node.Ports.Where(port => port.Alignment == alignment).ToList();
        for (var i = 0; i < names.Count && i < ports.Count; i++)
        {
            if (string.Equals(names[i], portName, StringComparison.Ordinal))
            {
                return ports[i];
            }
        }

        // Nodes with no declared ports fall back to a single default port of each side.
        return ports.FirstOrDefault();
    }

    private static ValidationMessageModel Warning(string message, string? node, string? componentType)
        => new()
        {
            Severity = ValidationSeverity.Warning,
            Source = ValidationSource.Composition,
            Message = message,
            NodeName = node,
            ComponentType = componentType,
        };
}
