using FluxFlow.Studio.UI.Features.Designer.Projects;
using Microsoft.JSInterop;
using MudBlazor;
using FluxFlow.Authoring;
using Microsoft.AspNetCore.Components;

namespace FluxFlow.DesignerApp.Services;

public sealed class BrowserStudioFileAccess(IJSRuntime js, IDialogService dialogs, IEnumerable<IComponentDescription> components) : IStudioFileAccess
{
    public async Task<StudioFile?> OpenAsync(string extension) =>
        await js.InvokeAsync<StudioFile?>("studioFiles.open", extension);

    public async Task<StudioFile?> OpenVerbatimTextAsync() =>
        await js.InvokeAsync<StudioFile?>("studioFiles.open", "", false, true);

    public async Task<StudioImageFile?> OpenImageAsync() =>
        await js.InvokeAsync<StudioImageFile?>("studioFiles.open", ".png,.jpg,.jpeg", true);

    public async Task<StudioFile?> SaveAsync(StudioFile file, bool saveAs = false)
    {
        // Browsers download a copy; both Save and Save As allow choosing its name.
        if (await Show("Save file", new DialogParameters<SaveFileDialog> { { d => d.FileName, file.Name } }) is not { } name) return null;
        await js.InvokeVoidAsync("studioFiles.save", name, file.Content);
        return new StudioFile(name, file.Content);
    }

    public Task PublishApplicationAsync(string name, string configuration, int startupTimeoutSeconds) =>
        DownloadApplicationAsync("Publish application", name, configuration, $"api/runs/publish?startupTimeoutSeconds={startupTimeoutSeconds}", forRunner: true);

    public Task GenerateApplicationSourceAsync(string configuration) =>
        DownloadApplicationAsync("Generate App.cs", "App.cs", configuration, "api/runs/generate", forRunner: false);

    public Task PublishCompiledApplicationAsync(string configuration) => PublishCompiledApplicationAsync(configuration, 11);

    public Task PublishCompiledApplicationAsync(string configuration, int targetMajor) =>
        DownloadApplicationAsync("Publish compiled application", "App.exe", configuration, $"api/runs/publish-compiled?targetMajor={targetMajor}", forRunner: false,
            subtitle: $"Windows x64 · .NET {targetMajor}", compiledApplication: true);

    private async Task DownloadApplicationAsync(string title, string name, string configuration, string endpoint, bool forRunner,
        string? subtitle = null, bool compiledApplication = false)
    {
        var paths = PublishSourceFiles.Find(new DesignerConfiguration(configuration), components, forRunner);
        var parameters = new DialogParameters<SaveFileDialog>
        {
            { d => d.FileName, name },
            { d => d.Subtitle, subtitle },
            { d => d.SubmitLabel, compiledApplication ? "Publish" : "Save" },
            { d => d.CompiledApplication, compiledApplication },
            { d => d.SourcePaths, paths },
            { d => d.Publish, (Func<string, ElementReference, Task<string?>>)Publish }
        };
        await Show(title, parameters, backdropClick: false);

        Task<string?> Publish(string selected, ElementReference fields) =>
            js.InvokeAsync<string?>("studioFiles.downloadApplication", Path.ChangeExtension(selected, Path.GetExtension(name)), configuration, endpoint, fields).AsTask();
    }

    private async Task<string?> Show(string title, DialogParameters<SaveFileDialog> parameters, bool backdropClick = true)
    {
        var dialog = await dialogs.ShowAsync<SaveFileDialog>(title, parameters, new DialogOptions { MaxWidth = MaxWidth.Small, FullWidth = true, BackdropClick = backdropClick });
        return await dialog.Result is { Canceled: false, Data: string selected } ? selected : null;
    }
}
