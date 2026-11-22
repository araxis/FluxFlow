using System.IO.Compression;
using System.Text;
using FluxFlow.Authoring;
using FluxFlow.Execution.Plugins;
using FluxFlow.Environments;
using System.Text.Json;

namespace FluxFlow.Studio.Publishing;

public static class ApplicationPackage
{
    public static void Write(Stream destination, string configuration, int startupTimeoutSeconds, InstalledPlugins plugins,
        CancellationToken cancellation = default) => Write(destination, configuration, startupTimeoutSeconds, plugins, null, cancellation);

    public static void Write(Stream destination, string configuration, int startupTimeoutSeconds, InstalledPlugins plugins,
        IReadOnlyDictionary<string, Func<Stream>>? sourceFiles, CancellationToken cancellation = default)
    {
        var environment = new EnvironmentProfile { StartupTimeoutSeconds = startupTimeoutSeconds };
        environment.Validate();
        var document = new DesignerConfiguration(configuration);
        var modules = plugins.Modules;
        var required = RequiredPlugins(document, plugins);
        var sources = PublishSourceFiles.Find(document, modules.SelectMany(m => m.Description.Components));
        var sourceEntries = sources.Select(p => "runner/" + p).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sourceDirectories = sourceEntries.SelectMany(PublishSourceFiles.Parents).ToHashSet(StringComparer.OrdinalIgnoreCase);
        ValidateSources(sources, sourceFiles);

        var runner = Path.Combine(AppContext.BaseDirectory, "application-runner");
        foreach (var fileName in new[] { "FluxFlow.Runner.dll", "FluxFlow.Runner.runtimeconfig.json", "FluxFlow.Runner.deps.json", "fluxflow-plugin.ps1" })
            if (!File.Exists(Path.Combine(runner, fileName)))
                throw new InvalidOperationException($"The .NET 11 Runner is missing '{fileName}'. Publish the Studio distribution and try again.");
        using var archive = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);
        using (var file = archive.CreateEntry("application.json").Open())
            file.Write(Encoding.UTF8.GetBytes(configuration));
        using (var file = archive.CreateEntry("environment.json").Open())
            JsonSerializer.Serialize(file, environment, EnvironmentJsonContext.Default.EnvironmentProfile);
        archive.CreateEntryFromFile(Path.Combine(AppContext.BaseDirectory, "start.ps1"), "start.ps1");
        AddDirectory(runner, "runner/");
        foreach (var path in sources)
        {
            cancellation.ThrowIfCancellationRequested();
            using var input = sourceFiles![path]();
            using var entry = archive.CreateEntry("runner/" + path, CompressionLevel.Fastest).Open();
            input.CopyTo(entry);
        }
        foreach (var id in required)
        {
            var package = plugins.PackageDirectory(id);
            // Folder installs must still satisfy the bundled installer's contract at publish time.
            foreach (var fileName in new[] { "fluxflow-plugin.json", $"lib/net8.0/{id}.dll", $"lib/net10.0/{id}.dll" })
                if (!File.Exists(Path.Combine(package, fileName)))
                    throw new InvalidOperationException($"Plugin '{id}' is missing '{fileName}'. Reinstall it before publishing.");
            AddDirectory(package, $"packages/{id}/");
        }

        void AddDirectory(string path, string prefix)
        {
            var directory = new DirectoryInfo(path);
            RejectLink(directory);
            foreach (var item in directory.EnumerateFileSystemInfos())
            {
                cancellation.ThrowIfCancellationRequested();
                RejectLink(item);
                var name = prefix + item.Name;
                if (sourceEntries.Contains(name) || item is FileInfo && sourceDirectories.Contains(name))
                    throw new InvalidOperationException($"Source file conflicts with Runner path '{name}'.");
                if (item is DirectoryInfo) AddDirectory(item.FullName, name + "/");
                else if (item.Name != ".install-source")
                    archive.CreateEntryFromFile(item.FullName, name, CompressionLevel.Fastest);
            }
        }
        static void RejectLink(FileSystemInfo item)
        {
            if (item.Attributes.HasFlag(FileAttributes.ReparsePoint))
                throw new IOException($"Linked files cannot be published: {item.Name}.");
        }
    }

    internal static SortedSet<string> RequiredPlugins(DesignerConfiguration document, InstalledPlugins plugins)
    {
        var modules = plugins.Modules;
        var components = modules.SelectMany(m => m.Description.Components.Select(c => (c.Palette.ComponentType, m.Description.Id)))
            .ToDictionary(c => c.ComponentType, c => c.Id, StringComparer.Ordinal);
        var resources = modules.SelectMany(m => m.Description.Connections.Select(c => (c.Type, m.Description.Id)))
            .ToDictionary(c => c.Type, c => c.Id, StringComparer.Ordinal);
        var required = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var workflow in document.WorkflowNames)
            foreach (var node in document.Nodes(workflow))
                Require(components, node.Type, node.Address.ToString());
        foreach (var resource in document.Resources())
            Require(resources, resource.Type, resource.Path);
        // File expressions can call extensions, so include installed function providers as well.
        foreach (var module in modules.Where(m => m.Functions.Any())) required.Add(module.Description.Id);
        return required;

        void Require(Dictionary<string, string> owners, string type, string address)
        {
            if (!owners.TryGetValue(type, out var owner))
                throw new InvalidOperationException($"Install the plugin for '{type}' used by '{address}' before publishing.");
            required.Add(owner);
        }
    }

    internal static void ValidateSources(string[] paths, IReadOnlyDictionary<string, Func<Stream>>? files)
    {
        foreach (var path in paths)
            if (files?.ContainsKey(path) != true) throw new InvalidOperationException($"Select the Source file for '{path}'.");
        if (files is not null && files.Count != paths.Length)
            throw new InvalidOperationException("The selected Source files do not match the application.");
    }
}
