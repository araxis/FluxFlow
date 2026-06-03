using System.Xml.Linq;
using Shouldly;
using Xunit;

namespace FluxFlow.Release.Tests;

public sealed class PackageFeedVerifyScriptTests
{
    [Fact]
    public async Task Feed_verify_script_prepares_isolated_consumer_project()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var package = GetConfigurationPackage(root);
        var version = ReadProjectVersion(root, package);
        var packageSource = Path.Combine(Path.GetTempPath(), $"fluxflow-package-source-{Guid.NewGuid():N}");
        var workDirectory = Path.Combine(Path.GetTempPath(), $"fluxflow-feed-verify-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(packageSource);

            var result = await ReleaseScriptRunner.RunAsync(
                root,
                "package-feed-verify.ps1",
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
            result.StandardOutput.ShouldContain($"PACKAGE_SOURCE={packageSource}");
            result.StandardOutput.ShouldContain("PACKAGE_CACHE=");

            var project = File.ReadAllText(Path.Combine(workDirectory, "FeedVerify.csproj"));
            project.ShouldContain($"<TargetFramework>net8.0</TargetFramework>");
            project.ShouldContain($"<PackageReference Include=\"{package.PackageId}\" Version=\"{version}\" />");
            project.ShouldContain($"<RestoreSources>{packageSource}</RestoreSources>");
            project.ShouldContain("<RestorePackagesPath>");

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
    public async Task Feed_verify_script_rejects_invalid_attempt_count()
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
                "package-feed-verify.ps1",
                "-PackageId",
                package.PackageId,
                "-Version",
                version,
                "-PackageSource",
                packageSource,
                "-Attempts",
                "0",
                "-PrepareOnly");

            result.ExitCode.ShouldNotBe(0);
            result.ToString().ShouldContain("Attempts must be at least 1");
        }
        finally
        {
            if (Directory.Exists(packageSource))
                Directory.Delete(packageSource, recursive: true);
        }
    }

    [Fact]
    public async Task Feed_verify_script_rejects_unknown_local_source()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var package = GetConfigurationPackage(root);
        var version = ReadProjectVersion(root, package);
        var packageSource = Path.Combine(Path.GetTempPath(), $"fluxflow-missing-source-{Guid.NewGuid():N}");

        var result = await ReleaseScriptRunner.RunAsync(
            root,
            "package-feed-verify.ps1",
            "-PackageId",
            package.PackageId,
            "-Version",
            version,
            "-PackageSource",
            packageSource,
            "-PrepareOnly");

        result.ExitCode.ShouldNotBe(0);
        result.ToString().ShouldContain("existing directory");
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
