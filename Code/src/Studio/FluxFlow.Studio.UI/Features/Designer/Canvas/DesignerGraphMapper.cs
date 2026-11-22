using Blazor.Diagrams;
using Blazor.Diagrams.Core.Anchors;
using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;
using FluxFlow.Application.Addressing;
using FluxFlow.Authoring;
using DiagramPort = Blazor.Diagrams.Core.Models.PortModel;

namespace FluxFlow.Studio.UI.Features.Designer.Canvas;

/// <summary>Projects supported configuration into the canvas. Unsupported content stays in the JSON document.</summary>
public static class DesignerGraphMapper
{
    private const int ColumnSpacing = 280;
    private const int RowSpacing = 300;

    private static Point GridPosition(int index) =>
        new(80 + index % 4 * ColumnSpacing, 100 + index / 4 * RowSpacing);

    internal static Point NextPalettePosition(BlazorDiagram diagram)
    {
        for (var index = 0; ; index++)
        {
            var slot = GridPosition(index);
            if (!diagram.Nodes.Any(node =>
                slot.X < node.Position.X + (node.Size?.Width ?? ColumnSpacing) &&
                slot.X + ColumnSpacing > node.Position.X &&
                slot.Y < node.Position.Y + (node.Size?.Height ?? RowSpacing) &&
                slot.Y + RowSpacing > node.Position.Y))
                return slot;
        }
    }

    public static IReadOnlyList<ValidationMessageModel> Load(BlazorDiagram diagram, DesignerConfiguration configuration,
        string workflow, IReadOnlyList<ConfigurationLink> links, Func<string, IComponentDescription?> description, ISet<ComponentAddress>? unavailable = null)
    {
        var messages = new List<ValidationMessageModel>();
        diagram.Links.Clear();
        diagram.Nodes.Clear();
        var nodes = new Dictionary<string, FlowNodeModel>(StringComparer.Ordinal);
        foreach (var node in configuration.Nodes(workflow))
        {
            var item = description(node.Type);
            if (item is null)
            {
                unavailable?.Add(node.Address);
                messages.Add(Warning($"'{node.Address}' uses '{node.Type}', which has no editor here. Its configuration is preserved."));
                continue;
            }
            var model = new FlowNodeModel(node.Address.ComponentName, item, GridPosition(nodes.Count), node.Values);
            model.Title = model.Presentation.DisplayName;
            nodes.Add(model.NodeName, model);
            diagram.Nodes.Add(model);
        }
        foreach (var link in links.Where(link => link.Source.ComponentAddress?.WorkflowName == workflow || link.Target.ComponentAddress?.WorkflowName == workflow))
        {
            var source = Find(link.Source, output: true, link.IsError);
            var target = Find(link.Target, output: false);
            if (source is null || target is null)
            {
                if (source is FlowPortModel sourcePort) sourcePort.HasPreservedLinks = true;
                if (target is FlowPortModel targetPort) targetPort.HasPreservedLinks = true;
                messages.Add(Warning($"Link '{link.Source}' to '{link.Target}' cannot be shown on this canvas and is preserved."));
                continue;
            }
            diagram.Links.Add(new FlowLinkModel(source, target, link));
        }
        return messages;

        DiagramPort? Find(PortAddress address, bool output, bool isError = false)
        {
            if (address.ComponentAddress is not { } component || component.WorkflowName != workflow ||
                !nodes.TryGetValue(component.ComponentName, out var node)) return null;
            var alignment = output ? PortAlignment.Right : PortAlignment.Left;
            return node.Ports.OfType<FlowPortModel>().FirstOrDefault(port => port.Alignment == alignment &&
                port.IsInput != output && port.IsError == isError && port.PortName == address.PortName);
        }
    }

    public static PortAddress? Endpoint(Anchor anchor, string workflow)
    {
        if (anchor is not SinglePortAnchor { Port: FlowPortModel port } || port.Parent is not FlowNodeModel node) return null;
        return ComponentAddress.Create(workflow, node.NodeName).Port(port.PortName);
    }

    private static ValidationMessageModel Warning(string message) => new()
        { Severity = ValidationSeverity.Warning, Source = ValidationSource.Composition, Message = message };
}
