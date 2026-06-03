using System.Xml.Linq;
using Shouldly;
using Xunit;

namespace FluxFlow.Release.Tests;

public sealed class PackageConsumerSmokeScriptTests
{
    [Fact]
    public async Task Consumer_smoke_script_prepares_throwaway_project()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var package = GetConfigurationPackage(root);
        var version = ReadProjectVersion(root, package);
        var packageSource = Path.Combine(Path.GetTempPath(), $"fluxflow-package-source-{Guid.NewGuid():N}");
        var workDirectory = Path.Combine(Path.GetTempPath(), $"fluxflow-consumer-smoke-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(packageSource);
            File.WriteAllText(Path.Combine(packageSource, $"{package.PackageId}.{version}.nupkg"), string.Empty);

            var result = await ReleaseScriptRunner.RunAsync(
                root,
                "package-consumer-smoke.ps1",
                "-PackageId",
                package.PackageId,
                "-Version",
                version,
                "-PackageSource",
                packageSource,
                "-WorkDirectory",
                workDirectory,
                "-PrepareOnly");

            result.ExitCode.ShouldBe(0, result.ToString());
            result.StandardOutput.ShouldContain($"WORK_DIR={workDirectory}");
            result.StandardOutput.ShouldContain($"{package.PackageId}.{version}.nupkg");

            var project = File.ReadAllText(Path.Combine(workDirectory, "ConsumerSmoke.csproj"));
            project.ShouldContain($"<TargetFramework>net8.0</TargetFramework>");
            project.ShouldContain($"<PackageReference Include=\"{package.PackageId}\" Version=\"{version}\" />");
            project.ShouldContain(packageSource);

            var program = File.ReadAllText(Path.Combine(workDirectory, "Program.cs"));
            program.ShouldContain($"var packageId = \"{package.PackageId}\";");
            program.ShouldContain("Assembly.Load(packageId)");
            program.ShouldContain("GetTypes()");
        }
        finally
        {
            if (Directory.Exists(packageSource))
                Directory.Delete(packageSource, recursive: true);

            if (Directory.Exists(workDirectory))
                Directory.Delete(workDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task Consumer_smoke_script_rejects_missing_package_file()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var package = GetConfigurationPackage(root);
        var version = ReadProjectVersion(root, package);
        var packageSource = Path.Combine(Path.GetTempPath(), $"fluxflow-package-source-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(packageSource);

            var result = await ReleaseScriptRunner.RunAsync(
                root,
                "package-consumer-smoke.ps1",
                "-PackageId",
                package.PackageId,
                "-Version",
                version,
                "-PackageSource",
                packageSource,
                "-PrepareOnly");

            result.ExitCode.ShouldNotBe(0);
            result.ToString().ShouldContain("Package file");
            result.ToString().ShouldContain("was not found");
        }
        finally
        {
            if (Directory.Exists(packageSource))
                Directory.Delete(packageSource, recursive: true);
        }
    }

    private static PackageManifestEntry GetConfigurationPackage(string root)
        => PackageManifest
            .Read(root)
            .Single(entry => entry.Alias == "components-configuration");

    private static string ReadProjectVersion(string root, PackageManifestEntry package)
    {
        var projectPath = Path.Combine(root, NormalizePath(package.Project));
        var project = XDocument.Load(projectPath);
        return project
            .Descendants()
            .Where(element => element.Name.LocalName == "Version")
            .Select(element => element.Value.Trim())
            .First(value => value.Length > 0);
    }

    private static string NormalizePath(string path)
        => path
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);
}
