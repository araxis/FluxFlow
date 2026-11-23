using System.Diagnostics;
using System.Xml.Linq;

namespace FluxFlow.Publishing;

public static class CompiledApplicationPublisher
{
    public static Task PublishAsync(Stream destination, string feed,
        Func<Stream, CancellationToken, Task> writeSource, CancellationToken cancellation) =>
        PublishAsync(destination, feed, "win-x64", writeSource, cancellation);

    public static async Task PublishAsync(Stream destination, string feed, string runtime,
        Func<Stream, CancellationToken, Task> writeSource, CancellationToken cancellation)
    {
        var executableName = runtime switch
        {
            "win-x64" => "App.exe",
            "linux-x64" => "App",
            _ => throw new ArgumentException($"Unsupported publish runtime '{runtime}'.", nameof(runtime))
        };
        var build = CreateBuildWorkspace();
        try
        {
            // Only generated code enters the SDK workspace. Selected Source files stay in the generator workspace.
            using (var source = File.Create(Path.Combine(build.FullName, "App.cs")))
                await writeSource(source, cancellation);
            await File.WriteAllTextAsync(Path.Combine(build.FullName, "global.json"),
                """{"sdk":{"version":"11.0.100","rollForward":"latestFeature","allowPrerelease":false}}""", cancellation);
            var config = GetRestoreConfig(feed, build);
            var command = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = build.FullName, UseShellExecute = false, CreateNoWindow = true,
                ArgumentList = { "publish", "App.cs", "-c", "Release", "-r", runtime, "--no-self-contained",
                    "--disable-build-servers", "--configfile", config, "-o", "out" }
            };
            command.Environment["NUGET_PACKAGES"] = Path.Combine(build.FullName, "p");
            // The file-based SDK stores intermediate assemblies under the process temp directory.
            var scratch = build.CreateSubdirectory("t").FullName;
            command.Environment["TEMP"] = command.Environment["TMP"] = command.Environment["TMPDIR"] = scratch;
            await DotnetProcess.RunAsync(command, cancellation);
            using var executable = File.OpenRead(Path.Combine(build.FullName, "out", executableName));
            await executable.CopyToAsync(destination, cancellation);
        }
        finally { build.Delete(recursive: true); }
    }

    private static string GetRestoreConfig(string feed, DirectoryInfo build)
    {
        var config = Path.Combine(feed, "NuGet.Config");
        if (File.Exists(config)) return config;

        using var template = typeof(CompiledApplicationPublisher).Assembly.GetManifestResourceStream("DefaultNuGetConfig")!;
        var document = XDocument.Load(template);
        var local = document.Root!.Element("packageSources")!.Elements("add").Single(e => (string?)e.Attribute("value") == ".");
        local.SetAttributeValue("value", Path.GetFullPath(feed));
        config = Path.Combine(build.FullName, "NuGet.Config");
        document.Save(config);
        return config;
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
}
