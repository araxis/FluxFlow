using System.Xml.Linq;
using Shouldly;
using Xunit;

namespace FluxFlow.Release.Tests;

public sealed class PackageListScriptTests
{
    [Fact]
    public async Task Package_list_script_outputs_manifest_aliases_and_versions()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var entries = PackageManifest.Read(root);
        var package = GetConfigurationPackage(entries);
        var version = ReadProjectVersion(root, package);

        var result = await ReleaseScriptRunner.RunAsync(root, "list-package-releases.ps1");

        result.ExitCode.ShouldBe(0, result.ToString());
        result.StandardOutput.ShouldContain($"PACKAGE_COUNT={entries.Count}");
        result.StandardOutput.ShouldContain("ALIAS\tVERSION\tTAG\tPACKAGE_ID\tPROJECT");
        result.StandardOutput.ShouldContain(
            $"{package.Alias}\t{version}\t{package.TagPrefix}-v{version}\t{package.PackageId}\t{package.Project}");
    }

    [Fact]
    public async Task Package_list_script_can_filter_to_one_package()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var entries = PackageManifest.Read(root);
        var package = GetConfigurationPackage(entries);
        var otherPackage = entries.First(entry => entry.Alias != package.Alias);
        var version = ReadProjectVersion(root, package);

        var result = await ReleaseScriptRunner.RunAsync(
            root,
            "list-package-releases.ps1",
            "-Package",
            package.Alias);

        result.ExitCode.ShouldBe(0, result.ToString());
        result.StandardOutput.ShouldContain("PACKAGE_COUNT=1");
        result.StandardOutput.ShouldContain(
            $"{package.Alias}\t{version}\t{package.TagPrefix}-v{version}\t{package.PackageId}\t{package.Project}");
        result.StandardOutput.ShouldNotContain($"{otherPackage.Alias}\t");
    }

    [Fact]
    public async Task Package_list_script_rejects_missing_manifest()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var missingManifest = Path.Combine(Path.GetTempPath(), $"fluxflow-missing-manifest-{Guid.NewGuid():N}.json");

        var result = await ReleaseScriptRunner.RunAsync(
            root,
            "list-package-releases.ps1",
            "-ManifestPath",
            missingManifest);

        result.ExitCode.ShouldNotBe(0);
        result.ToString().ShouldContain("was not found");
    }

    private static PackageManifestEntry GetConfigurationPackage(IReadOnlyList<PackageManifestEntry> entries)
        => entries.Single(entry => entry.Alias == "components-configuration");

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
