using FluxFlow.DesignerHost;

namespace FluxFlow.DesignerApp.Features.Designer.Pages;

public partial class DesignerPage
{
    private NodeInspectorModel? _inspector;

    protected override void OnInitialized() => Graph.Changed += OnGraphChanged;

    private void AddNode(string componentType)
    {
        var item = Catalog.Palette.FirstOrDefault(entry => entry.ComponentType == componentType);
        if (item is not null)
        {
            Graph.AddNode(item);
        }
    }

    private void OnGraphChanged()
    {
        var componentType = Graph.SelectedNode?.ComponentType;
        _inspector = componentType is null ? null : Catalog.Inspector(componentType);
        InvokeAsync(StateHasChanged);
    }

    private void ZoomToFit() => Graph.Diagram.ZoomToFit(40);

    public void Dispose() => Graph.Changed -= OnGraphChanged;
}
