using Blazor.Diagrams;
using Blazor.Diagrams.Core.Anchors;
using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;
using FluxFlow.Components.Designer.Persistence;
using FluxFlow.Composition.Addressing;
using FluxFlow.DesignerHost;
using DiagramPort = Blazor.Diagrams.Core.Models.PortModel;

namespace FluxFlow.DesignerApp.Features.Designer.Canvas;

/// <summary>
/// Maps one rendered workflow to and from the canonical Designer application
/// models. Canvas geometry remains host-only and is never persisted into an
/// application definition.
/// </summary>
public static class DesignerGraphMapper
{
    public static DesignerWorkflowCanvas ToWorkflow(BlazorDiagram diagram, string workflowName)
    {
        var components = diagram.Nodes.OfType<FlowNodeModel>()
            .ToDictionary(
                node => node.NodeName,
                node => new DesignerComponent
                {
                    Type = node.ComponentType,
                    Properties = new Dictionary<string, System.Text.Json.JsonElement>(
                        node.Configuration,
                        StringComparer.Ordinal)
                },
                StringComparer.Ordinal);

        var links = new List<DesignerApplicationLink>();
        foreach (var diagramLink in diagram.Links)
        {
            var from = Endpoint(diagramLink.Source);
            var to = Endpoint(diagramLink.Target);
            if (from is null || to is null)
                continue;

            var source = ApplicationAddress.WorkflowPort(workflowName, from.Value.Node, from.Value.Port);
            var target = ApplicationAddress.WorkflowPort(workflowName, to.Value.Node, to.Value.Port);
            links.Add(diagramLink is FlowLinkModel loaded
                ? new DesignerApplicationLink(
                    source,
                    target,
                    loaded.Condition,
                    loaded.DeclarationSide)
                : DesignerApplicationLink.Create(source, target));
        }

        return new DesignerWorkflowCanvas(
            new DesignerWorkflow
            {
                Name = workflowName,
                Components = components
            },
            links);
    }

    public static IReadOnlyList<ValidationMessageModel> Load(
        BlazorDiagram diagram,
        DesignerWorkflow workflow,
        IEnumerable<DesignerApplicationLink> links,
        Func<string, PaletteItemModel?> palette)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentNullException.ThrowIfNull(links);

        var messages = new List<ValidationMessageModel>();
        diagram.Links.Clear();
        diagram.Nodes.Clear();

        var byName = new Dictionary<string, FlowNodeModel>(StringComparer.Ordinal);
        var index = 0;
        foreach (var (componentName, component) in workflow.Components)
        {
            var item = palette(component.Type);
            if (item is null)
            {
                messages.Add(Warning(
                    $"Unknown component type '{component.Type}'; component '{componentName}' was not rendered.",
                    componentName,
                    component.Type));
                continue;
            }

            var column = index % 4;
            var row = index / 4;
            var model = new FlowNodeModel(
                componentName,
                item,
                new Point(120 + column * 220, 80 + row * 150));
            foreach (var property in component.Properties)
                model.Configuration[property.Key] = property.Value;

            diagram.Nodes.Add(model);
            byName[componentName] = model;
            index++;
        }

        foreach (var link in links)
        {
            if (!IsLocalWorkflowLink(link, workflow.Name))
            {
                messages.Add(Warning(
                    $"Link {link.Source} -> {link.Target} is outside the active workflow canvas and was preserved without rendering.",
                    null,
                    null));
                continue;
            }

            var fromName = link.Source.Segments[1];
            var fromPortName = link.Source.Segments[2];
            var toName = link.Target.Segments[1];
            var toPortName = link.Target.Segments[2];
            if (!byName.TryGetValue(fromName, out var fromNode) ||
                !byName.TryGetValue(toName, out var toNode))
            {
                messages.Add(Warning(
                    $"Link {link.Source} -> {link.Target} references a component that was not rendered.",
                    null,
                    null));
                continue;
            }

            var fromPort = PortByName(
                fromNode,
                PortAlignment.Right,
                fromNode.OutputPortNames,
                fromPortName);
            var toPort = InputPortByName(toNode, toPortName);
            if (fromPort is null || toPort is null)
            {
                messages.Add(Warning(
                    $"Link {link.Source} -> {link.Target} references an unknown port.",
                    null,
                    null));
                continue;
            }

            diagram.Links.Add(new FlowLinkModel(
                fromPort,
                toPort,
                link.DeclarationSide,
                link.Condition));
        }

        return messages;
    }

    public static bool IsLocalWorkflowLink(DesignerApplicationLink link, string workflowName)
        => link.Source.Kind == ApplicationAddressKind.WorkflowPort &&
           string.Equals(link.Source.Segments[0], workflowName, StringComparison.Ordinal) &&
           string.Equals(link.Target.Segments[0], workflowName, StringComparison.Ordinal);

    private static (string Node, string Port)? Endpoint(Anchor anchor)
    {
        if (anchor is not SinglePortAnchor portAnchor || portAnchor.Port.Parent is not FlowNodeModel node)
            return null;

        return (node.NodeName, PortName(node, portAnchor.Port));
    }

    private static string PortName(FlowNodeModel node, DiagramPort port)
    {
        var names = port.Alignment switch
        {
            PortAlignment.Right => node.OutputPortNames,
            PortAlignment.Top => node.SignalInputPortNames,
            _ => node.InputPortNames
        };
        var index = node.Ports.Where(candidate => candidate.Alignment == port.Alignment).ToList().IndexOf(port);
        if (index >= 0 && index < names.Count)
            return names[index];

        return port.Alignment == PortAlignment.Right ? "Output" : "Input";
    }

    private static DiagramPort? InputPortByName(FlowNodeModel node, string portName)
        => PortByName(node, PortAlignment.Left, node.InputPortNames, portName)
           ?? PortByName(node, PortAlignment.Top, node.SignalInputPortNames, portName);

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
                return ports[i];
        }

        return names.Count == 0 ? ports.FirstOrDefault() : null;
    }

    private static ValidationMessageModel Warning(string message, string? node, string? componentType)
        => new()
        {
            Severity = ValidationSeverity.Warning,
            Source = ValidationSource.Composition,
            Message = message,
            NodeName = node,
            ComponentType = componentType
        };
}

public sealed record DesignerWorkflowCanvas(
    DesignerWorkflow Workflow,
    IReadOnlyList<DesignerApplicationLink> Links);
