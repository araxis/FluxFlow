using System.Text.Json;
using Blazor.Diagrams;
using Blazor.Diagrams.Core.Anchors;
using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;
using Blazor.Diagrams.Core.Models.Base;
using FluxFlow.Composition;
using FluxFlow.DesignerApp.Features.Designer.Canvas;
using FluxFlow.DesignerHost;

namespace FluxFlow.DesignerApp.Features.Designer;

/// <summary>
/// Owns the single <see cref="BlazorDiagram"/> for the designer canvas and the
/// current node selection, and bridges the canvas to the host-model persistence
/// mapping. The UI reads this state; it does not reach into the diagram directly.
/// <see cref="Changed"/> fires when the graph or selection changes.
/// </summary>
public sealed class DesignerGraphState
{
    public const string WorkflowName = "main";

    private readonly DesignerCatalog _catalog;
    private int _added;

    public DesignerGraphState(DesignerCatalog catalog)
    {
        _catalog = catalog;
        Diagram = new BlazorDiagram();
        Diagram.SelectionChanged += OnSelectionChanged;
        Diagram.Links.Added += OnLinkAdded;
    }

    public BlazorDiagram Diagram { get; }

    public FlowNodeModel? SelectedNode { get; private set; }

    public int NodeCount => Diagram.Nodes.Count;

    public bool HasSelection => Diagram.GetSelectedModels().Any();

    public event Action? Changed;

    /// <summary>Raised with a reason when a drawn link is rejected as invalid.</summary>
    public event Action<string>? LinkRejected;

    public FlowNodeModel AddNode(PaletteItemModel item)
    {
        _added++;
        var offset = 36 * (_added % 8);
        var node = new FlowNodeModel(MakeName(item), item, new Point(140 + offset, 80 + offset));
        Diagram.Nodes.Add(node);
        Changed?.Invoke();
        return node;
    }

    public void Clear()
    {
        Diagram.Links.Clear();
        Diagram.Nodes.Clear();
        _added = 0;
        SelectedNode = null;
        Changed?.Invoke();
    }

    /// <summary>Remove the selected nodes and links. Removing a node also removes its links.</summary>
    public void DeleteSelected()
    {
        var selected = Diagram.GetSelectedModels().ToList();
        foreach (var link in selected.OfType<BaseLinkModel>())
        {
            Diagram.Links.Remove(link);
        }

        foreach (var node in selected.OfType<NodeModel>())
        {
            Diagram.Nodes.Remove(node);
        }

        SelectedNode = null;
        Changed?.Invoke();
    }

    private void OnLinkAdded(BaseLinkModel link) => link.TargetAttached += OnLinkTargetAttached;

    private void OnLinkTargetAttached(BaseLinkModel link)
    {
        if (IsValidConnection(link, out var reason))
        {
            Changed?.Invoke();
            return;
        }

        Diagram.Links.Remove(link);
        LinkRejected?.Invoke(reason);
    }

    private static bool IsValidConnection(BaseLinkModel link, out string reason)
    {
        reason = string.Empty;

        // Only enforce rules once both ends land on component ports.
        if (link.Source is not SinglePortAnchor source || source.Port.Parent is not FlowNodeModel sourceNode ||
            link.Target is not SinglePortAnchor target || target.Port.Parent is not FlowNodeModel targetNode)
        {
            return true;
        }

        if (ReferenceEquals(sourceNode, targetNode))
        {
            reason = "A node cannot connect to itself.";
            return false;
        }

        if (source.Port.Alignment != PortAlignment.Right || target.Port.Alignment != PortAlignment.Left)
        {
            reason = "Links must go from an output port (right) to an input port (left).";
            return false;
        }

        return true;
    }

    /// <summary>Serialize the current canvas as a composition definition (JSON).</summary>
    public string ToJson()
    {
        var graph = DesignerGraphMapper.ToGraph(Diagram, WorkflowName);
        var definition = GraphDefinitionMapper.ToDefinition(graph);
        return JsonSerializer.Serialize(definition, CompositionDefinitionJson.CreateSerializerOptions());
    }

    /// <summary>Rebuild the canvas from a composition definition (JSON), returning any load warnings.</summary>
    public IReadOnlyList<ValidationMessageModel> LoadJson(string json)
    {
        var definition = JsonSerializer.Deserialize<CompositionDefinition>(
            json, CompositionDefinitionJson.CreateSerializerOptions())
            ?? throw new InvalidOperationException("The JSON did not contain a composition definition.");

        var graphs = GraphDefinitionMapper.FromDefinition(definition);
        var graph = graphs.FirstOrDefault(candidate =>
                        string.Equals(candidate.WorkflowName, WorkflowName, StringComparison.Ordinal))
                    ?? graphs.FirstOrDefault();

        if (graph is null)
        {
            Clear();
            return [];
        }

        var messages = DesignerGraphMapper.Load(Diagram, graph, _catalog.Find);
        _added = Diagram.Nodes.Count;
        SelectedNode = null;
        Changed?.Invoke();
        return messages;
    }

    private void OnSelectionChanged(SelectableModel _)
    {
        SelectedNode = Diagram.GetSelectedModels().OfType<FlowNodeModel>().LastOrDefault();
        Changed?.Invoke();
    }

    private string MakeName(PaletteItemModel item)
    {
        var baseName = item.PreferredNodeName ?? item.ComponentType.Split('.')[^1];
        return $"{baseName}-{_added}";
    }
}
