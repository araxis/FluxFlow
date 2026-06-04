using System.Xml.Linq;
using Shouldly;
using Xunit;

namespace FluxFlow.Release.Tests;

public sealed class PackageReleaseDryRunScriptTests
{
    [Fact]
    public async Task Release_dry_run_script_prepares_resolved_package()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var package = GetConfigurationPackage(root);
        var version = ReadProjectVersion(root, package);
        var packageSource = Path.Combine(Path.GetTempPath(), $"fluxflow-dry-run-packages-{Guid.NewGuid():N}");

        try
        {
            var result = await ReleaseScriptRunner.RunAsync(
                root,
                "package-release-dry-run.ps1",
                "-Package",
                package.Alias,
                "-PackageSource",
                packageSource,
                "-PrepareOnly");

            result.ExitCode.ShouldBe(0, result.ToString());
            result.StandardOutput.ShouldContain($"DRY_RUN_PACKAGE_ALIAS={package.Alias}");
            result.StandardOutput.ShouldContain($"DRY_RUN_PACKAGE_ID={package.PackageId}");
            result.StandardOutput.ShouldContain($"DRY_RUN_PACKAGE_PROJECT={package.Project}");
            result.StandardOutput.ShouldContain($"DRY_RUN_PACKAGE_VERSION={version}");
            result.StandardOutput.ShouldContain($"DRY_RUN_RELEASE_TAG={package.TagPrefix}-v{version}");
            result.StandardOutput.ShouldContain($"DRY_RUN_PACKAGE_SOURCE={packageSource}");
            Directory.Exists(packageSource).ShouldBeTrue();
        }
        finally
        {
            if (Directory.Exists(packageSource))
                Directory.Delete(packageSource, recursive: true);
        }
    }

    [Fact]
    public async Task Release_dry_run_script_rejects_invalid_configuration()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var package = GetConfigurationPackage(root);
        var packageSource = Path.Combine(Path.GetTempPath(), $"fluxflow-dry-run-packages-{Guid.NewGuid():N}");

        try
        {
            var result = await ReleaseScriptRunner.RunAsync(
                root,
                "package-release-dry-run.ps1",
                "-Package",
                package.Alias,
                "-PackageSource",
                packageSource,
                "-Configuration",
                "../Release",
                "-PrepareOnly");

            result.ExitCode.ShouldNotBe(0);
            result.ToString().ShouldContain("Configuration");
            result.ToString().ShouldContain("not supported");
        }
        finally
        {
            if (Directory.Exists(packageSource))
                Directory.Delete(packageSource, recursive: true);
        }
    }

    [Fact]
    public async Task Release_dry_run_script_rejects_file_package_source()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var package = GetConfigurationPackage(root);
        var packageSource = Path.Combine(Path.GetTempPath(), $"fluxflow-dry-run-source-{Guid.NewGuid():N}.tmp");

        try
        {
            File.WriteAllText(packageSource, "");

            var result = await ReleaseScriptRunner.RunAsync(
                root,
                "package-release-dry-run.ps1",
                "-Package",
                package.Alias,
                "-PackageSource",
                packageSource,
                "-PrepareOnly");

            result.ExitCode.ShouldNotBe(0);
            result.ToString().ShouldContain("must be a directory");
        }
        finally
        {
            if (File.Exists(packageSource))
                File.Delete(packageSource);
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
