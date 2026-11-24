using System.Diagnostics;
using FluxFlow.Authoring;
using FluxFlow.Publishing;
using FluxFlow.Execution.Plugins;

namespace FluxFlow.Studio.Publishing;

public static class ApplicationSource
{
    public static Task WriteAsync(Stream destination, string configuration, InstalledPlugins plugins,
        IReadOnlyDictionary<string, Func<Stream>>? sourceFiles, CancellationToken cancellation = default)
        => WriteAsync(destination, configuration, plugins, sourceFiles, null, null, cancellation);

    private static async Task WriteAsync(Stream destination, string configuration, InstalledPlugins plugins,
        IReadOnlyDictionary<string, Func<Stream>>? sourceFiles, string? framework, string? runtime, CancellationToken cancellation)
    {
        var workspace = Directory.CreateTempSubdirectory("fluxflow-generate-");
        try
        {
            var export = await StageExportAsync(workspace, configuration, plugins, sourceFiles, cancellation);
            var output = Path.Combine(workspace.FullName, "App.cs");
            var command = CliCommand("generate", export, output);
            var feed = Path.Combine(command.WorkingDirectory, "feed");
            var packages = Directory.Exists(feed) ? Directory.GetFiles(feed, "*.nupkg").Order(StringComparer.Ordinal).ToArray() : [];
            if (packages.Length == 0) throw new InvalidOperationException("The Studio distribution contains no C# mapping packages.");
            if (framework is not null)
            {
                command.ArgumentList.Add("--framework");
                command.ArgumentList.Add(framework);
            }
            if (runtime == "linux-x64")
            {
                command.ArgumentList.Add("--runtime");
                command.ArgumentList.Add(runtime);
            }
            foreach (var package in packages) command.ArgumentList.Add(package);
            await DotnetProcess.RunAsync(command, cancellation);
            using var generated = File.OpenRead(output);
            await generated.CopyToAsync(destination, cancellation);
        }
        finally { workspace.Delete(recursive: true); }
    }

    private static ProcessStartInfo CliCommand(params string[] args)
    {
        var tools = Path.Combine(AppContext.BaseDirectory, "app-generator");
        var cli = Path.Combine(tools, "FluxFlow.Cli.dll");
        if (!File.Exists(cli)) throw new InvalidOperationException("The Studio distribution is missing the FluxFlow CLI.");
        var command = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = tools,
            ArgumentList = { "--roll-forward", "Major", cli }
        };
        foreach (var arg in args) command.ArgumentList.Add(arg);
        return command;
    }

    private static async Task<string> StageExportAsync(DirectoryInfo workspace, string configuration, InstalledPlugins plugins,
        IReadOnlyDictionary<string, Func<Stream>>? sourceFiles, CancellationToken cancellation)
    {
        var document = new DesignerConfiguration(configuration);
        ApplicationPackage.RequiredPlugins(document, plugins);
        var paths = PublishSourceFiles.Find(document, plugins.Modules.SelectMany(m => m.Description.Components), forRunner: false);
        ApplicationPackage.ValidateSources(paths, sourceFiles);
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
        var export = Path.Combine(inputs, Guid.NewGuid().ToString("N") + ".json");
        using (var stream = new FileStream(export, FileMode.CreateNew, FileAccess.Write))
        using (var writer = new StreamWriter(stream))
            await writer.WriteAsync(configuration.AsMemory(), cancellation);
        return export;
    }

    public static async Task ManageWindowsServiceAsync(string action, string name, int major, bool automatic,
        string configuration, InstalledPlugins plugins, IReadOnlyDictionary<string, Func<Stream>>? sources, CancellationToken cancellation)
    {
        if (!OperatingSystem.IsWindows()) throw new InvalidOperationException("Windows services require a Windows Studio host.");
        if (action is "start" or "stop")
        {
            cancellation.ThrowIfCancellationRequested();
            await RunServiceCommand(CliCommand("service", action, name)).ConfigureAwait(false);
            return;
        }
        if (action != "install" || major is not (10 or 11)) throw new ArgumentException("Invalid Windows service action or target.");
        var workspace = Directory.CreateTempSubdirectory("fluxflow-install-");
        try
        {
            var export = await StageExportAsync(workspace, configuration, plugins, sources, cancellation);
            var command = CliCommand("service", "install", name, export, "--framework", $"net{major}.0",
                "--startup", automatic ? "automatic" : "manual", "--feed", Path.Combine(AppContext.BaseDirectory, "app-generator", "feed"));
            cancellation.ThrowIfCancellationRequested();
            // The installer owns rollback after launch, even if the Studio client disconnects.
            await RunServiceCommand(command).ConfigureAwait(false);
        }
        finally { workspace.Delete(recursive: true); }
    }

    // Desktop shutdown may close its dispatcher while the installer is completing.
    private static Task RunServiceCommand(ProcessStartInfo command) =>
        Task.Run(() => DotnetProcess.RunAsync(command, CancellationToken.None));

    public static Task PublishCompiledAsync(Stream destination, string configuration, InstalledPlugins plugins,
        IReadOnlyDictionary<string, Func<Stream>>? sourceFiles, CancellationToken cancellation = default)
        => PublishCompiledAsync(destination, configuration, plugins, sourceFiles, 11, cancellation);

    public static Task PublishCompiledAsync(Stream destination, string configuration, InstalledPlugins plugins,
        IReadOnlyDictionary<string, Func<Stream>>? sourceFiles, int targetMajor, CancellationToken cancellation = default)
        => PublishCompiledAsync(destination, configuration, plugins, sourceFiles, targetMajor, "win-x64", cancellation);

    public static async Task PublishCompiledAsync(Stream destination, string configuration, InstalledPlugins plugins,
        IReadOnlyDictionary<string, Func<Stream>>? sourceFiles, int targetMajor, string runtime, CancellationToken cancellation = default)
    {
        var framework = targetMajor switch
        {
            10 => "net10.0",
            11 => "net11.0",
            _ => throw new ArgumentOutOfRangeException(nameof(targetMajor), "Choose .NET 10 or .NET 11.")
        };
        var feed = Path.Combine(AppContext.BaseDirectory, "app-generator", "feed");
        await CompiledApplicationPublisher.PublishAsync(destination, feed, runtime,
            (source, token) => WriteAsync(source, configuration, plugins, sourceFiles, framework, runtime, token), cancellation);
    }
}
