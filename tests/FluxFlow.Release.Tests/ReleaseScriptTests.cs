using System.Xml.Linq;
using Shouldly;
using Xunit;

namespace FluxFlow.Release.Tests;

public sealed class ReleaseScriptTests
{
    [Fact]
    public async Task Resolve_package_release_resolves_alias_and_writes_environment_file()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var package = GetConfigurationPackage(root);
        var version = ReadProjectVersion(root, package);
        var environmentPath = Path.Combine(Path.GetTempPath(), $"fluxflow-release-{Guid.NewGuid():N}.env");

        try
        {
            var result = await ReleaseScriptRunner.RunAsync(
                root,
                "resolve-package-release.ps1",
                "-Package",
                package.Alias,
                "-ManifestPath",
                Path.Combine(root, "eng", "packages.json"),
                "-EnvironmentPath",
                environmentPath);

            result.ExitCode.ShouldBe(0, result.ToString());
            result.StandardOutput.ShouldContain($"PACKAGE_ALIAS={package.Alias}");
            result.StandardOutput.ShouldContain($"PACKAGE_ID={package.PackageId}");
            result.StandardOutput.ShouldContain($"PACKAGE_PROJECT={package.Project}");
            result.StandardOutput.ShouldContain($"PACKAGE_VERSION={version}");
            result.StandardOutput.ShouldContain($"RELEASE_TAG={package.TagPrefix}-v{version}");
            result.StandardOutput.ShouldContain($"IS_PRERELEASE={version.Contains('-')}");

            var environment = File.ReadAllText(environmentPath);
            environment.ShouldContain($"PACKAGE_ALIAS={package.Alias}");
            environment.ShouldContain($"PACKAGE_ID={package.PackageId}");
            environment.ShouldContain($"PACKAGE_VERSION={version}");
        }
        finally
        {
            if (File.Exists(environmentPath))
                File.Delete(environmentPath);
        }
    }

    [Fact]
    public async Task Resolve_package_release_resolves_tag_name()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var package = GetConfigurationPackage(root);
        var version = ReadProjectVersion(root, package);

        var result = await ReleaseScriptRunner.RunAsync(
            root,
            "resolve-package-release.ps1",
            "-RefName",
            $"{package.TagPrefix}-v{version}",
            "-ManifestPath",
            Path.Combine(root, "eng", "packages.json"));

        result.ExitCode.ShouldBe(0, result.ToString());
        result.StandardOutput.ShouldContain($"PACKAGE_ALIAS={package.Alias}");
        result.StandardOutput.ShouldContain($"PACKAGE_ID={package.PackageId}");
        result.StandardOutput.ShouldContain($"PACKAGE_VERSION={version}");
        result.StandardOutput.ShouldContain($"RELEASE_TAG={package.TagPrefix}-v{version}");
    }

    [Fact]
    public async Task Resolve_package_release_rejects_mismatched_package_and_tag()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var package = GetConfigurationPackage(root);
        var otherPackage = PackageManifest
            .Read(root)
            .First(entry => entry.PackageId != package.PackageId);
        var version = ReadProjectVersion(root, package);

        var result = await ReleaseScriptRunner.RunAsync(
            root,
            "resolve-package-release.ps1",
            "-Package",
            otherPackage.Alias,
            "-RefName",
            $"{package.TagPrefix}-v{version}",
            "-ManifestPath",
            Path.Combine(root, "eng", "packages.json"));

        result.ExitCode.ShouldNotBe(0);
        result.ToString().ShouldContain("does not match tag package");
    }

    [Fact]
    public async Task Resolve_package_release_rejects_version_mismatch()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var package = GetConfigurationPackage(root);

        var result = await ReleaseScriptRunner.RunAsync(
            root,
            "resolve-package-release.ps1",
            "-Package",
            package.Alias,
            "-Version",
            "9.9.9",
            "-ManifestPath",
            Path.Combine(root, "eng", "packages.json"));

        result.ExitCode.ShouldNotBe(0);
        result.ToString().ShouldContain("does not match project version");
    }

    [Fact]
    public async Task Get_release_notes_writes_current_package_section_only()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var package = GetConfigurationPackage(root);
        var version = ReadProjectVersion(root, package);
        var outputPath = Path.Combine(Path.GetTempPath(), $"fluxflow-release-notes-{Guid.NewGuid():N}.md");

        try
        {
            var result = await ReleaseScriptRunner.RunAsync(
                root,
                "get-release-notes.ps1",
                "-PackageName",
                package.NotesName,
                "-Version",
                version,
                "-ChangelogPath",
                Path.Combine(root, "CHANGELOG.md"),
                "-OutputPath",
                outputPath);

            result.ExitCode.ShouldBe(0, result.ToString());
            File.Exists(outputPath).ShouldBeTrue();

            var notes = File.ReadAllText(outputPath);
            notes.ShouldContain("Hardens configuration option path normalization.");
            notes.ShouldContain(package.PackageId);
            notes.ShouldNotContain("## ");
        }
        finally
        {
            if (File.Exists(outputPath))
                File.Delete(outputPath);
        }
    }

    [Fact]
    public async Task Get_release_notes_rejects_missing_package_section()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var package = GetConfigurationPackage(root);
        var outputPath = Path.Combine(Path.GetTempPath(), $"fluxflow-release-notes-{Guid.NewGuid():N}.md");

        try
        {
            var result = await ReleaseScriptRunner.RunAsync(
                root,
                "get-release-notes.ps1",
                "-PackageName",
                package.NotesName,
                "-Version",
                "9.9.9",
                "-ChangelogPath",
                Path.Combine(root, "CHANGELOG.md"),
                "-OutputPath",
                outputPath);

            result.ExitCode.ShouldNotBe(0);
            result.ToString().ShouldContain("does not contain a section");
            File.Exists(outputPath).ShouldBeFalse();
        }
        finally
        {
            if (File.Exists(outputPath))
                File.Delete(outputPath);
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
