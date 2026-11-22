using System.Diagnostics;
using FluxFlow.Authoring;
using FluxFlow.Publishing;
using FluxFlow.Execution.Plugins;

namespace FluxFlow.Studio.Publishing;

public static class ApplicationSource
{
    public static Task WriteAsync(Stream destination, string configuration, InstalledPlugins plugins,
        IReadOnlyDictionary<string, Func<Stream>>? sourceFiles, CancellationToken cancellation = default)
        => WriteAsync(destination, configuration, plugins, sourceFiles, null, cancellation);

    private static async Task WriteAsync(Stream destination, string configuration, InstalledPlugins plugins,
        IReadOnlyDictionary<string, Func<Stream>>? sourceFiles, string? framework, CancellationToken cancellation)
    {
        var document = new DesignerConfiguration(configuration);
        ApplicationPackage.RequiredPlugins(document, plugins);
        var paths = PublishSourceFiles.Find(document, plugins.Modules.SelectMany(m => m.Description.Components), forRunner: false);
        ApplicationPackage.ValidateSources(paths, sourceFiles);
        var tools = Path.Combine(AppContext.BaseDirectory, "app-generator");
        var cli = Path.Combine(tools, "FluxFlow.Cli.dll");
        if (!File.Exists(cli)) throw new InvalidOperationException("The Studio distribution is missing the FluxFlow CLI.");
        var feed = Path.Combine(tools, "feed");
        var packages = Directory.GetFiles(feed, "*.nupkg").Order(StringComparer.Ordinal).ToArray();
        if (packages.Length == 0) throw new InvalidOperationException("The Studio distribution contains no C# mapping packages.");

        var workspace = Directory.CreateTempSubdirectory("fluxflow-generate-");
        try
        {
            var inputs = workspace.CreateSubdirectory("inputs").FullName;
            foreach (var path in paths)
            {
                var target = Path.Combine(inputs, path);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                using var input = sourceFiles![path]();
                using var staged = File.Create(target);
                await input.CopyToAsync(staged, cancellation);
            }
            // A Source may itself be called application.json or App.cs.
            var export = Path.Combine(inputs, Path.GetRandomFileName());
            using (var stream = new FileStream(export, FileMode.CreateNew, FileAccess.Write))
            using (var writer = new StreamWriter(stream))
                await writer.WriteAsync(configuration.AsMemory(), cancellation);
            var output = Path.Combine(workspace.FullName, "App.cs");
            var command = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false, CreateNoWindow = true,
                WorkingDirectory = tools,
                ArgumentList = { "--roll-forward", "Major", cli, "generate", export, output }
            };
            if (framework is not null)
            {
                command.ArgumentList.Add("--framework");
                command.ArgumentList.Add(framework);
            }
            foreach (var package in packages) command.ArgumentList.Add(package);
            await DotnetProcess.RunAsync(command, cancellation);
            using var generated = File.OpenRead(output);
            await generated.CopyToAsync(destination, cancellation);
        }
        finally { workspace.Delete(recursive: true); }
    }

    public static Task PublishCompiledAsync(Stream destination, string configuration, InstalledPlugins plugins,
        IReadOnlyDictionary<string, Func<Stream>>? sourceFiles, CancellationToken cancellation = default)
        => PublishCompiledAsync(destination, configuration, plugins, sourceFiles, 11, cancellation);

    public static async Task PublishCompiledAsync(Stream destination, string configuration, InstalledPlugins plugins,
        IReadOnlyDictionary<string, Func<Stream>>? sourceFiles, int targetMajor, CancellationToken cancellation = default)
    {
        var framework = targetMajor switch
        {
            10 => "net10.0",
            11 => "net11.0",
            _ => throw new ArgumentOutOfRangeException(nameof(targetMajor), "Choose .NET 10 or .NET 11.")
        };
        var feed = Path.Combine(AppContext.BaseDirectory, "app-generator", "feed");
        await CompiledApplicationPublisher.PublishAsync(destination, feed,
            (source, token) => WriteAsync(source, configuration, plugins, sourceFiles, framework, token), cancellation);
    }
}
