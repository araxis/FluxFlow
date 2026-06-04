using System.Xml.Linq;
using Shouldly;
using Xunit;

namespace FluxFlow.Release.Tests;

public sealed class PackageReleasePreflightScriptTests
{
    [Fact]
    public async Task Release_preflight_script_prints_current_release_commands()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var package = GetConfigurationPackage(root);
        var version = ReadProjectVersion(root, package);

        var result = await ReleaseScriptRunner.RunAsync(
            root,
            "package-release-preflight.ps1",
            "-Package",
            package.Alias);

        result.ExitCode.ShouldBe(0, result.ToString());
        result.StandardOutput.ShouldContain($"PREFLIGHT_PACKAGE_ALIAS={package.Alias}");
        result.StandardOutput.ShouldContain($"PREFLIGHT_PACKAGE_ID={package.PackageId}");
        result.StandardOutput.ShouldContain($"PREFLIGHT_PACKAGE_PROJECT={package.Project}");
        result.StandardOutput.ShouldContain($"PREFLIGHT_PACKAGE_VERSION={version}");
        result.StandardOutput.ShouldContain($"PREFLIGHT_RELEASE_TAG={package.TagPrefix}-v{version}");
        result.StandardOutput.ShouldContain($"PREFLIGHT_CHANGELOG_NAME={package.NotesName}");
        result.StandardOutput.ShouldContain("PREFLIGHT_CHANGELOG_OK=True");
        result.StandardOutput.ShouldContain(
            $"PREFLIGHT_DRY_RUN_COMMAND=./eng/package-release-dry-run.ps1 -Package {package.Alias} -Version {version}");
        result.StandardOutput.ShouldContain(
            $"PREFLIGHT_FAST_DRY_RUN_COMMAND=./eng/package-release-dry-run.ps1 -Package {package.Alias} -Version {version} -SkipSolutionBuild");
        result.StandardOutput.ShouldContain(
            $"PREFLIGHT_TAG_COMMAND=./eng/package-release-tag.ps1 -Package {package.Alias} -Version {version}");
        result.StandardOutput.ShouldContain(
            $"PREFLIGHT_TAG_PUSH_COMMAND=./eng/package-release-tag.ps1 -Package {package.Alias} -Version {version} -Push");
    }

    [Fact]
    public async Task Release_preflight_script_rejects_missing_changelog_section()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var package = GetConfigurationPackage(root);
        var changelogPath = Path.Combine(Path.GetTempPath(), $"fluxflow-empty-changelog-{Guid.NewGuid():N}.md");

        try
        {
            File.WriteAllText(changelogPath, "# Changelog");

            var result = await ReleaseScriptRunner.RunAsync(
                root,
                "package-release-preflight.ps1",
                "-Package",
                package.Alias,
                "-ChangelogPath",
                changelogPath);

            result.ExitCode.ShouldNotBe(0);
            result.ToString().ShouldContain("does not contain a section");
            result.StandardOutput.ShouldNotContain("PREFLIGHT_TAG_COMMAND");
        }
        finally
        {
            if (File.Exists(changelogPath))
                File.Delete(changelogPath);
        }
    }

    [Fact]
    public async Task Release_preflight_script_rejects_version_mismatch()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var package = GetConfigurationPackage(root);

        var result = await ReleaseScriptRunner.RunAsync(
            root,
            "package-release-preflight.ps1",
            "-Package",
            package.Alias,
            "-Version",
            "9.9.9");

        result.ExitCode.ShouldNotBe(0);
        result.ToString().ShouldContain("does not match project version");
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
