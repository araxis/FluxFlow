using System.Xml.Linq;
using Shouldly;
using Xunit;

namespace FluxFlow.Release.Tests;

[Collection(ReleaseProcessCollection.Name)]
public sealed class PackageReleaseTagScriptTests
{
    [Fact]
    public async Task Release_tag_script_prepares_resolved_tag()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var package = GetNodesPackage(root);
        var version = ReadProjectVersion(root, package);

        var result = await ReleaseScriptRunner.RunAsync(
            root,
            "package-release-tag.ps1",
            "-Package",
            package.Alias,
            "-PrepareOnly");

        result.ExitCode.ShouldBe(0, result.ToString());
        result.StandardOutput.ShouldContain($"TAG_PACKAGE_ALIAS={package.Alias}");
        result.StandardOutput.ShouldContain($"TAG_PACKAGE_ID={package.PackageId}");
        result.StandardOutput.ShouldContain($"TAG_PACKAGE_VERSION={version}");
        result.StandardOutput.ShouldContain($"TAG_NAME={package.TagPrefix}-v{version}");
        result.StandardOutput.ShouldContain($"TAG_MESSAGE={package.PackageId} {version}");
    }

    [Fact]
    public async Task Release_tag_script_uses_custom_tag_message()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var package = GetNodesPackage(root);
        const string message = "Prepared package release";

        var result = await ReleaseScriptRunner.RunAsync(
            root,
            "package-release-tag.ps1",
            "-Package",
            package.Alias,
            "-TagMessage",
            message,
            "-PrepareOnly");

        result.ExitCode.ShouldBe(0, result.ToString());
        result.StandardOutput.ShouldContain($"TAG_MESSAGE={message}");
    }

    [Fact]
    public async Task Release_tag_script_rejects_invalid_remote_name()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var package = GetNodesPackage(root);

        var result = await ReleaseScriptRunner.RunAsync(
            root,
            "package-release-tag.ps1",
            "-Package",
            package.Alias,
            "-Push",
            "-Remote",
            "../origin",
            "-PrepareOnly");

        result.ExitCode.ShouldNotBe(0);
        result.ToString().ShouldContain("Remote");
        result.ToString().ShouldContain("not supported");
    }

    [Fact]
    public void Release_tag_script_checks_public_availability_before_release_validation()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var script = File.ReadAllText(Path.Combine(root, "eng", "package-release-tag.ps1"));

        script.ShouldContain("[string] $PublicPackageSource");
        script.ShouldContain(
            "$availabilityPath = Join-Path $repoRoot \"eng/package-release-availability.ps1\"");

        var availabilityIndex = RequiredIndexOf(script, "& $availabilityPath");
        var expectedStateIndex = RequiredIndexOf(script, "-ExpectedState Missing", availabilityIndex);
        var availabilityCall = script[availabilityIndex..(expectedStateIndex + "-ExpectedState Missing".Length)];

        availabilityCall.ShouldContain("-Package $packageAlias");
        availabilityCall.ShouldContain("-Version $packageVersion");
        availabilityCall.ShouldContain("-PackageSource $PublicPackageSource");
        availabilityCall.ShouldContain("-ExpectedState Missing");

        var releaseNotesIndex = RequiredIndexOf(script, "Assert-ReleaseNotesExist", expectedStateIndex);
        var dryRunIndex = RequiredIndexOf(script, "& $dryRunPath", expectedStateIndex);
        var tagCreationIndex = RequiredIndexOf(script, "Invoke-Step $tool @(\"tag\"", expectedStateIndex);

        availabilityIndex.ShouldBeLessThan(releaseNotesIndex);
        availabilityIndex.ShouldBeLessThan(dryRunIndex);
        availabilityIndex.ShouldBeLessThan(tagCreationIndex);
    }

    private static PackageManifestEntry GetNodesPackage(string root)
        => PackageManifest
            .Read(root)
            .Single(entry => entry.Alias == "nodes");

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

    private static int RequiredIndexOf(string content, string value, int startIndex = 0)
    {
        var index = content.IndexOf(value, startIndex, StringComparison.Ordinal);
        index.ShouldBeGreaterThanOrEqualTo(0, $"Release tag script must contain '{value}'.");
        return index;
    }
}
