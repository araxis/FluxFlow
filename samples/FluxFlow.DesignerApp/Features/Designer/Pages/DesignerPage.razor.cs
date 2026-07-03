using FluxFlow.DesignerApp.Features.Designer.Components;
using FluxFlow.DesignerHost;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace FluxFlow.DesignerApp.Features.Designer.Pages;

public partial class DesignerPage
{
    private NodeInspectorModel? _inspector;

    [Inject] private IDialogService DialogService { get; set; } = default!;

    [Inject] private ISnackbar Snackbar { get; set; } = default!;

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

    private async Task Save()
    {
        var parameters = new DialogParameters<GraphJsonDialog>
        {
            { dialog => dialog.Json, Graph.ToJson() },
            { dialog => dialog.ReadOnly, true },
        };
        var options = new DialogOptions { MaxWidth = MaxWidth.Medium, FullWidth = true };
        await DialogService.ShowAsync<GraphJsonDialog>("Composition definition", parameters, options);
    }

    private async Task Load()
    {
        var parameters = new DialogParameters<GraphJsonDialog>
        {
            { dialog => dialog.Json, string.Empty },
            { dialog => dialog.ReadOnly, false },
        };
        var options = new DialogOptions { MaxWidth = MaxWidth.Medium, FullWidth = true };
        var reference = await DialogService.ShowAsync<GraphJsonDialog>("Load composition JSON", parameters, options);
        var result = await reference.Result;
        if (result is null || result.Canceled || result.Data is not string json || string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        try
        {
            var messages = Graph.LoadJson(json);
            if (messages.Count == 0)
            {
                Snackbar.Add("Loaded composition.", Severity.Success);
            }
            else
            {
                foreach (var message in messages)
                {
                    Snackbar.Add(message.Message, Severity.Warning);
                }
            }
        }
        catch (Exception exception)
        {
            Snackbar.Add($"Could not load composition: {exception.Message}", Severity.Error);
        }
    }

    private void Clear() => Graph.Clear();

    private void ZoomToFit() => Graph.Diagram.ZoomToFit(40);

    public void Dispose() => Graph.Changed -= OnGraphChanged;
}
