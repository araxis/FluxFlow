using System.Text.Json;
using FluxFlow.Plugins.Management;

namespace FluxFlow.Execution.Plugins;

// Keep the original filename so existing companion settings are retained.
internal sealed class PluginSourceStore(string root)
{
    private readonly string _path = Path.Combine(root, "nuget-sources.json");

    public async Task<PluginSource[]> ReadAsync()
    {
        try
        {
            await using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            var sources = await JsonSerializer.DeserializeAsync(stream, PluginManagementJson.Default.PluginSourceArray);
            return Validate(sources ?? throw new InvalidDataException("Invalid plugin source settings."));
        }
        catch (FileNotFoundException) { return []; }
        catch (DirectoryNotFoundException) { return []; }
        catch (Exception ex) when (ex is JsonException or ArgumentException) { throw new InvalidDataException("Cannot read the saved plugin sources.", ex); }
    }

    public async Task SaveAsync(PluginSourceChange change)
    {
        if (change.Updated is null || change.Original is null) throw new ArgumentException("Missing source settings.");
        var updated = Validate(change.Updated);

        Directory.CreateDirectory(root);
        using var writeLock = File.Open(_path + ".lock", FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
        var original = await ReadAsync();
        if (!original.SequenceEqual(change.Original))
            throw new InvalidOperationException("Sources changed in another Studio. Reopen Settings before saving.");
        foreach (var folder in updated.Where(s => s.Kind == PluginSourceKind.Folder))
        {
            // A previously saved folder may be offline or replaced; unrelated sources must remain editable.
            if (!original.Any(s => s.Kind == PluginSourceKind.Folder && s.Url == folder.Url) && File.Exists(folder.Url))
                throw new ArgumentException("Choose a folder, not a file.");
        }
        var temp = _path + "." + Guid.NewGuid().ToString("N");
        try
        {
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(updated, PluginManagementJson.Default.PluginSourceArray));
            File.Move(temp, _path, overwrite: true);
        }
        finally { File.Delete(temp); }
    }

    private static PluginSource[] Validate(PluginSource[] sources)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { PluginSource.Default.Name };
        var urls = new HashSet<Uri> { new(PluginSource.Default.Url) };
        var folders = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var updated = new List<PluginSource>();
        foreach (var source in sources)
        {
            var name = source?.Name?.Trim();
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Enter a name for each source.");
            if (!names.Add(name)) throw new ArgumentException($"The source name '{name}' is already in use.");
            if (source!.Kind == PluginSourceKind.Folder)
            {
                var path = LocalPluginManager.LocalPath(source.Url?.Trim());
                if (!folders.Add(path)) throw new ArgumentException("This folder is already in the list.");
                updated.Add(source with { Name = name, Url = path });
                continue;
            }
            if (source.Kind != PluginSourceKind.NuGet) throw new ArgumentException("Choose NuGet or Folder.");
            var url = NuGetPluginDownload.FeedUri(source!.Url?.Trim());
            if (url.Query.Length != 0 || url.Fragment.Length != 0)
                throw new ArgumentException("Save the feed URL without a query string or fragment.");
            if (!urls.Add(url)) throw new ArgumentException("This feed URL is already in the list.");
            updated.Add(new(name, url.AbsoluteUri));
        }
        return [.. updated];
    }
}
