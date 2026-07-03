using Blazor.Diagrams;
using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models.Base;
using FluxFlow.DesignerApp.Features.Designer.Canvas;
using FluxFlow.DesignerHost;

namespace FluxFlow.DesignerApp.Features.Designer;

/// <summary>
/// Owns the single <see cref="BlazorDiagram"/> for the designer canvas and the
/// current node selection. The UI reads this state; it does not reach into the
/// diagram directly. <see cref="Changed"/> fires when the selection changes so
/// the page can refresh the inspector.
/// </summary>
public sealed class DesignerGraphState
{
    private int _added;

    public DesignerGraphState()
    {
        Diagram = new BlazorDiagram();
        Diagram.SelectionChanged += OnSelectionChanged;
    }

    public BlazorDiagram Diagram { get; }

    public FlowNodeModel? SelectedNode { get; private set; }

    public int NodeCount => Diagram.Nodes.Count;

    public event Action? Changed;

    public FlowNodeModel AddNode(PaletteItemModel item)
    {
        _added++;
        var offset = 36 * (_added % 8);
        var node = new FlowNodeModel(MakeName(item), item, new Point(140 + offset, 80 + offset));
        Diagram.Nodes.Add(node);
        Changed?.Invoke();
        return node;
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
