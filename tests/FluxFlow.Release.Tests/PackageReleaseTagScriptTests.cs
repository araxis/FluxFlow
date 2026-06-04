using System.Xml.Linq;
using Shouldly;
using Xunit;

namespace FluxFlow.Release.Tests;

public sealed class PackageReleaseTagScriptTests
{
    [Fact]
    public async Task Release_tag_script_prepares_resolved_tag()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var package = GetConfigurationPackage(root);
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
        var package = GetConfigurationPackage(root);
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
        var package = GetConfigurationPackage(root);

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
