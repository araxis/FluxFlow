using FluxFlow.Publishing;
using FluxFlow.Studio.UI.Features.Designer.Projects;
using FluxFlow.Execution.Plugins;
using FluxFlow.Studio.Publishing;
using FluxFlow.Authoring;

namespace FluxFlow.Studio.Desktop.Services;

internal sealed class NativeStudioFileAccess(IDesktopFileDialogs dialogs, InstalledPlugins? plugins = null) : IStudioFileAccess, IAsyncDisposable
{
    private readonly CancellationTokenSource _closing = new();
    private readonly SemaphoreSlim _generatedWrite = new(1, 1);
    private InstalledPlugins Plugins => plugins ?? throw new InvalidOperationException("The plugin store is unavailable.");

    public Task<StudioFile?> OpenAsync(string extension) => OpenTextAsync(extension, false);
    public Task<StudioFile?> OpenVerbatimTextAsync() => OpenTextAsync("", true);

    private async Task<StudioFile?> OpenTextAsync(string extension, bool verbatim)
    {
        var path = await dialogs.OpenAsync(extension);
        if (path is null) return null;
        CheckSize(path, 10);
        var text = verbatim
            ? new System.Text.UTF8Encoding(false, true).GetString(await File.ReadAllBytesAsync(path))
            : await File.ReadAllTextAsync(path);
        return new(Path.GetFileName(path), text, path);
    }

    public async Task<StudioImageFile?> OpenImageAsync()
    {
        var path = await dialogs.OpenAsync(".png,.jpg,.jpeg");
        if (path is null) return null;
        CheckSize(path, 2);
        var mediaType = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            _ => throw new ArgumentException("Choose a PNG or JPEG image.")
        };
        return new(Path.GetFileName(path), mediaType, Convert.ToBase64String(await File.ReadAllBytesAsync(path)));
    }

    public async Task<StudioFile?> SaveAsync(StudioFile file, bool saveAs = false)
    {
        var path = saveAs || file.Location is null ? await dialogs.SaveAsync(file.Name) : file.Location;
        if (path is null) return null;
        await AtomicFile.WriteAsync(path, file.Content);
        return new(Path.GetFileName(path), file.Content, path);
    }

    private static void CheckSize(string path, int megabytes)
    {
        if (new FileInfo(path).Length > megabytes * 1024 * 1024)
            throw new ArgumentException($"Files must be {megabytes} MB or smaller.");
    }

    public async Task PublishApplicationAsync(string name, string configuration, int startupTimeoutSeconds)
    {
        if (await dialogs.SaveAsync(name) is not { } path) return;
        if (await SelectSourcesAsync(path, configuration, forRunner: true) is not { } sources) return;
        await AtomicFile.WriteAsync(path, temporary => Task.Run(() =>
        {
            using var output = File.Create(temporary);
            ApplicationPackage.Write(output, configuration, startupTimeoutSeconds, Plugins, sourceFiles: sources);
        }));
    }

    public Task GenerateApplicationSourceAsync(string configuration) => SaveGeneratedAsync(configuration, null, "App.cs");
    public Task PublishCompiledApplicationAsync(string configuration) => PublishCompiledApplicationAsync(configuration, 11);
    public Task PublishCompiledApplicationAsync(string configuration, int targetMajor) => PublishCompiledApplicationAsync(configuration, targetMajor, "App.exe");
    public Task PublishCompiledApplicationAsync(string configuration, int targetMajor, string suggestedName) =>
        PublishCompiledApplicationAsync(configuration, new CompiledPublishTarget(targetMajor), suggestedName);

    public Task PublishCompiledApplicationAsync(string configuration, CompiledPublishTarget target, string suggestedName) =>
        SaveGeneratedAsync(configuration, target, suggestedName);

    private async Task SaveGeneratedAsync(string configuration, CompiledPublishTarget? target, string suggestedName)
    {
        var cancellation = _closing.Token;
        var extension = target is null ? ".cs" : target.IsLinux ? null : ".exe";
        if (await dialogs.SaveAsync(suggestedName, useExtension: extension is not null) is not { } path) return;
        cancellation.ThrowIfCancellationRequested();
        if (extension is not null && !string.Equals(Path.GetExtension(path), extension, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Choose a destination with a {extension} extension.");
        if (await SelectSourcesAsync(path, configuration, forRunner: false, cancellation) is not { } sources) return;
        cancellation.ThrowIfCancellationRequested();
        await _generatedWrite.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            // The writer must be able to finish cleanup while the window's dispatcher is closing.
            await Task.Run(() => AtomicFile.WriteAsync(path, WriteAsync, cancellation), cancellation).ConfigureAwait(false);
        }
        finally { _generatedWrite.Release(); }

        async Task WriteAsync(string temporary)
        {
            using var output = File.Create(temporary);
            if (target is not null) await ApplicationSource.PublishCompiledAsync(output, configuration, Plugins, sources, target.Major, target.Runtime, cancellation);
            else await ApplicationSource.WriteAsync(output, configuration, Plugins, sources, cancellation);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _closing.CancelAsync().ConfigureAwait(false);
        await _generatedWrite.WaitAsync().ConfigureAwait(false);
        _generatedWrite.Dispose();
        _closing.Dispose();
    }

    public async Task ManageWindowsServiceAsync(StudioServiceAction action, string configuration, IReadOnlyDictionary<string, Func<Stream>> sources)
    {
        await _generatedWrite.WaitAsync(_closing.Token);
        try
        {
            await ApplicationSource.ManageWindowsServiceAsync(action.Action, action.Name, action.Major, action.Automatic,
                configuration, Plugins, sources, _closing.Token).ConfigureAwait(false);
        }
        finally { _generatedWrite.Release(); }
    }

    private async Task<Dictionary<string, Func<Stream>>?> SelectSourcesAsync(string path, string configuration, bool forRunner, CancellationToken cancellation = default)
    {
        var paths = PublishSourceFiles.Find(new DesignerConfiguration(configuration), Plugins.Modules.SelectMany(m => m.Description.Components), forRunner);
        var sources = new Dictionary<string, Func<Stream>>(StringComparer.Ordinal);
        foreach (var target in paths)
        {
            cancellation.ThrowIfCancellationRequested();
            var selected = await dialogs.OpenAsync("", $"Select Source file for {target}");
            if (selected is null) return null;
            if (string.Equals(Path.GetFullPath(path), Path.GetFullPath(selected), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Choose a destination different from the Source file '{target}'.");
            sources.Add(target, () => File.OpenRead(selected));
        }
        return sources;
    }
}
