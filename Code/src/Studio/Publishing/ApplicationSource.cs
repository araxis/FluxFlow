using System.Diagnostics;
using FluxFlow.Authoring;
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
            await RunDotnetAsync(command, cancellation);
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
        var build = CreateBuildWorkspace();
        try
        {
            // Only generated code enters the SDK workspace. Selected Source files stay in the generator workspace.
            using (var source = File.Create(Path.Combine(build.FullName, "App.cs")))
                await WriteAsync(source, configuration, plugins, sourceFiles, framework, cancellation);
            await File.WriteAllTextAsync(Path.Combine(build.FullName, "global.json"),
                """{"sdk":{"version":"11.0.100","rollForward":"latestFeature","allowPrerelease":false}}""", cancellation);
            var config = Path.Combine(AppContext.BaseDirectory, "app-generator", "feed", "NuGet.Config");
            var command = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = build.FullName, UseShellExecute = false, CreateNoWindow = true,
                ArgumentList = { "publish", "App.cs", "-c", "Release", "-r", "win-x64", "--no-self-contained",
                    "--disable-build-servers", "--configfile", config, "-o", "out" }
            };
            command.Environment["NUGET_PACKAGES"] = Path.Combine(build.FullName, "p");
            // The file-based SDK stores intermediate assemblies under the process temp directory.
            var scratch = build.CreateSubdirectory("t").FullName;
            command.Environment["TEMP"] = command.Environment["TMP"] = command.Environment["TMPDIR"] = scratch;
            await RunDotnetAsync(command, cancellation);
            using var executable = File.OpenRead(Path.Combine(build.FullName, "out", "App.exe"));
            await executable.CopyToAsync(destination, cancellation);
        }
        finally { build.Delete(recursive: true); }
    }

    private static DirectoryInfo CreateBuildWorkspace()
    {
        // Leave room under MAX_PATH for the SDK and NuGet's package/version/framework/assembly suffix.
        var temp = Path.GetTempPath();
        if (OperatingSystem.IsWindows() && temp.Length > 100)
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var localTemp = Path.Combine(local, "Temp");
            var prefix = Path.Combine(localTemp, $"ff-{Environment.ProcessId}-");
            if (local.Length > 0 && prefix.Length < Path.Combine(temp, "ff-").Length && Directory.Exists(localTemp))
            {
                try
                {
                    return Directory.CreateDirectory(prefix + Path.GetRandomFileName());
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // A redirected profile can have a read-only local Temp. Keep its configured TEMP usable.
                }
            }
        }
        return Directory.CreateTempSubdirectory("ff-");
    }

    private static async Task RunDotnetAsync(ProcessStartInfo command, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        command.RedirectStandardError = command.RedirectStandardOutput = true;
        using var process = new Process { StartInfo = command };
        try { process.Start(); }
        catch (System.ComponentModel.Win32Exception error)
        {
            throw new InvalidOperationException($"Could not start dotnet on the Studio host. {error.Message}", error);
        }
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync(cancellation); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            throw;
        }
        var output = await stdout;
        var errors = await stderr;
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"dotnet exited with code {process.ExitCode}.\n{output}{errors}".TrimEnd());
    }
}
