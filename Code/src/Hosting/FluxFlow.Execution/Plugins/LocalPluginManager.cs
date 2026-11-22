using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using FluxFlow.Plugins.Management;

namespace FluxFlow.Execution.Plugins;

internal sealed class LocalPluginManager(string root) : IPluginManager
{
    private readonly PluginSourceStore _sources = new(root);
    public Task<PluginSource[]> GetSourcesAsync() => _sources.ReadAsync();
    public Task SaveSourcesAsync(PluginSourceChange change) => _sources.SaveAsync(change);

    internal Dictionary<string, string> LoadErrors { get; } = new(StringComparer.OrdinalIgnoreCase);

    public async Task<PluginEntry[]> ListAsync()
    {
        var entries = JsonSerializer.Deserialize(await Run("List", "-NextStart", "-AsJson").ConfigureAwait(false),
            PluginManagementJson.Default.PluginEntryArray) ?? throw new InvalidDataException("The installer returned no plugin list.");
        return entries.Select(e => e with { LoadError = LoadErrors.GetValueOrDefault(e.Id) }).ToArray();
    }

    internal static string LocalPath(string? localPath)
    {
        if (string.IsNullOrWhiteSpace(localPath) || !Path.IsPathFullyQualified(localPath) ||
            localPath.StartsWith(@"\\", StringComparison.Ordinal) || localPath.StartsWith("//", StringComparison.Ordinal))
            throw new ArgumentException("Enter a full local path on the companion computer.");
        var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(localPath));
        if (path.StartsWith(@"\\", StringComparison.Ordinal)) throw new ArgumentException("Choose a local path.");
        return path;
    }

    public Task InstallAsync(string localPath) => Run("Install", "-NextStart", "-Source", LocalPath(localPath));

    public async Task<string[]> ListFolderAsync(PluginSource source)
    {
        if (source.Kind != PluginSourceKind.Folder || !(await GetSourcesAsync()).Contains(source))
            throw new ArgumentException("The folder source changed or was removed. Select it again.");
        var root = new DirectoryInfo(source.Url);
        if (!root.Exists) throw new DirectoryNotFoundException("The source folder is unavailable on the companion computer.");
        if (File.Exists(Path.Combine(root.FullName, "fluxflow-plugin.json"))) return [root.FullName];
        return root.EnumerateFileSystemInfos().Where(entry => !entry.Attributes.HasFlag(FileAttributes.ReparsePoint) &&
            (entry is DirectoryInfo ? File.Exists(Path.Combine(entry.FullName, "fluxflow-plugin.json")) :
            entry.Extension.Equals(".nupkg", StringComparison.OrdinalIgnoreCase) || entry.Extension.Equals(".zip", StringComparison.OrdinalIgnoreCase)))
            .Select(entry => entry.FullName).Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public Task InstallArchiveAsync(string name, Stream archive) => StageArchive(name, archive, []);

    public Task InstallFolderArchiveAsync(Stream archive) => StageArchive("folder.zip", archive, ["-SourceKind", "Folder"]);

    public Task InstallFeedAsync(PluginPackage package) => NuGetPluginDownload.Install(package,
        (stream, token) => StageArchive(package.Id + ".nupkg", stream, ["-SourceKind", "NuGet feed", "-Id", package.Id, "-Version", package.Version], token));

    private async Task StageArchive(string name, Stream archive, string[] identity, CancellationToken token = default)
    {
        var extension = Path.GetExtension(name).ToLowerInvariant();
        if (extension is not (".zip" or ".nupkg")) throw new ArgumentException("Choose a ZIP or NuGet package.");
        var path = Path.Combine(Path.GetTempPath(), $"fluxflow-plugin-{Guid.NewGuid():N}{extension}");
        try
        {
            await using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                var buffer = new byte[81920];
                int count;
                while ((count = await archive.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
                {
                    if (file.Length + count > IPluginManager.MaxUploadBytes) throw new InvalidDataException("Plugin archive exceeds 64 MiB.");
                    await file.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
                }
            }
            token.ThrowIfCancellationRequested();
            await Run("Install", ["-NextStart", "-BoundedArchive", "-Source", path, .. identity]).ConfigureAwait(false);
        }
        finally { File.Delete(path); }
    }

    public Task RemoveAsync(string id) => Run("Remove", "-NextStart", "-Id", id);
    public Task CancelAsync(string id) => Run("Cancel", "-NextStart", "-Id", id);

    internal void InstallBundledDefaults()
    {
        var bundle = Path.Combine(AppContext.BaseDirectory, "bundled-plugins");
        if (!Directory.Exists(Path.Combine(root, "active")) && Directory.Exists(bundle))
            Run("Initialize", "-Source", bundle).GetAwaiter().GetResult();
    }

    internal void ApplyPending()
    {
        var pending = Path.Combine(root, "pending");
        if (new[] { "add", "remove" }.Select(name => Path.Combine(pending, name))
            .Any(path => Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path).Any()))
            Run("ApplyPending").GetAwaiter().GetResult();
    }

    private async Task<string> Run(string action, params string[] arguments)
    {
        var start = new ProcessStartInfo("pwsh")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8, StandardErrorEncoding = System.Text.Encoding.UTF8
        };
        start.Environment["NO_COLOR"] = "1";
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-File", Path.Combine(AppContext.BaseDirectory, "fluxflow-plugin.ps1"),
            "-Action", action, "-Directory", root }.Concat(arguments))
            start.ArgumentList.Add(arg);
        try
        {
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the plugin installer.");
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            await Task.WhenAll(output, error, process.WaitForExitAsync()).ConfigureAwait(false);
            if (process.ExitCode != 0)
                throw new InvalidOperationException(error.Result.Trim().Split('\n').LastOrDefault()?.Trim().TrimStart('|').Trim()
                    is { Length: > 0 } message ? message : "The plugin installer failed.");
            return output.Result;
        }
        catch (Win32Exception ex) { throw new InvalidOperationException("The companion needs PowerShell 7 (pwsh) to manage plugins.", ex); }
    }
}
