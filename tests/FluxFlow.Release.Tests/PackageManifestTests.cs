using System.Text.RegularExpressions;
using System.Xml.Linq;
using Shouldly;
using Xunit;

namespace FluxFlow.Release.Tests;

public sealed class PackageManifestTests
{
    private static readonly Regex ReleaseTokenPattern = new("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.Compiled);

    [Fact]
    public void Package_manifest_matches_project_metadata()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var entries = PackageManifest.Read(root);
        var changelog = File.ReadAllText(Path.Combine(root, "CHANGELOG.md"));

        entries.ShouldNotBeEmpty();
        AssertUnique(entries.Select(entry => entry.Alias), "alias");
        AssertUnique(entries.Select(entry => entry.TagPrefix), "tag prefix");
        AssertUnique(entries.Select(entry => entry.PackageId), "package id");
        AssertUnique(entries.Select(entry => entry.Project), "project path");

        foreach (var entry in entries)
        {
            AssertManifestEntry(root, changelog, entry);
        }
    }

    [Fact]
    public void Package_manifest_contains_all_source_package_projects()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var entries = PackageManifest.Read(root);
        var manifestProjects = entries
            .Select(entry => NormalizeManifestPath(entry.Project))
            .ToHashSet(StringComparer.Ordinal);

        var sourceProjects = Directory
            .EnumerateFiles(Path.Combine(root, "src"), "*.csproj", SearchOption.AllDirectories)
            .Where(path => !string.IsNullOrWhiteSpace(ReadOptionalProperty(XDocument.Load(path), "PackageId")))
            .Select(path => NormalizeManifestPath(Path.GetRelativePath(root, path)))
            .Order(StringComparer.Ordinal)
            .ToArray();

        var missingProjects = sourceProjects
            .Where(project => !manifestProjects.Contains(project))
            .ToArray();

        missingProjects.ShouldBeEmpty("all source package projects must be listed in the release manifest.");
    }

    [Fact]
    public void Public_api_overview_mentions_every_manifest_package()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var entries = PackageManifest.Read(root);
        var overview = File.ReadAllText(Path.Combine(root, "docs", "14-public-api-overview.md"));

        var missingPackageIds = entries
            .Where(entry => !overview.Contains(entry.PackageId, StringComparison.Ordinal))
            .Select(entry => entry.PackageId)
            .Order(StringComparer.Ordinal)
            .ToArray();

        missingPackageIds.ShouldBeEmpty(
            "docs/14-public-api-overview.md must mention every shipped package id from eng/packages.json.");
    }

    [Fact]
    public void Package_versioning_docs_keep_project_files_and_manifest_as_source_of_truth()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var text = File.ReadAllText(Path.Combine(root, "docs", "11-package-versioning.md"));

        text.ShouldContain("Each packable project owns its own `<Version>`.");
        text.ShouldContain("`eng/packages.json` lists the shipped packages");
        text.ShouldContain("Packages in this repository move independently.");
        text.ShouldNotContain("Current stable engine and component release line");
        text.ShouldNotContain("Current stable pattern");
        text.ShouldNotContain("FluxFlow.Components.*        1.0.0");
    }

    private static void AssertManifestEntry(string root, string changelog, PackageManifestEntry entry)
    {
        entry.Alias.ShouldSatisfyAllConditions(
            value => string.IsNullOrWhiteSpace(value).ShouldBeFalse("alias is required"),
            value => ReleaseTokenPattern.IsMatch(value).ShouldBeTrue($"alias '{value}' must be a release token"));
        entry.TagPrefix.ShouldSatisfyAllConditions(
            value => string.IsNullOrWhiteSpace(value).ShouldBeFalse("tag prefix is required"),
            value => ReleaseTokenPattern.IsMatch(value).ShouldBeTrue($"tag prefix '{value}' must be a release token"));
        string.IsNullOrWhiteSpace(entry.PackageId).ShouldBeFalse("package id is required");
        string.IsNullOrWhiteSpace(entry.Project).ShouldBeFalse("project path is required");
        string.IsNullOrWhiteSpace(entry.NotesName).ShouldBeFalse("notes name is required");
        Path.IsPathRooted(entry.Project).ShouldBeFalse($"{entry.PackageId} project path must be relative");
        entry.Project.StartsWith("src/", StringComparison.Ordinal).ShouldBeTrue($"{entry.PackageId} project path must stay under src/");

        var projectPath = Path.GetFullPath(Path.Combine(root, NormalizePath(entry.Project)));
        File.Exists(projectPath).ShouldBeTrue($"{entry.PackageId} project file does not exist at {projectPath}");

        var project = XDocument.Load(projectPath);
        var projectDirectory = Path.GetDirectoryName(projectPath).ShouldNotBeNull();
        var packageId = ReadRequiredProperty(project, "PackageId", entry.PackageId);
        var version = ReadRequiredProperty(project, "Version", entry.PackageId);
        var readmeFile = ReadRequiredProperty(project, "PackageReadmeFile", entry.PackageId);
        ReadRequiredProperty(project, "PackageReleaseNotes", entry.PackageId);

        packageId.ShouldBe(entry.PackageId);
        AssertReadmeIsPacked(project, projectDirectory, readmeFile, entry.PackageId);

        var heading = $"## {entry.NotesName} {version}";
        changelog.Contains(heading, StringComparison.Ordinal)
            .ShouldBeTrue($"CHANGELOG.md must contain '{heading}'.");
        AssertChangelogSectionIsNotEmpty(changelog, heading);
    }

    private static void AssertChangelogSectionIsNotEmpty(string changelog, string heading)
    {
        var lines = changelog
            .Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .ToArray();

        var headingIndex = Array.FindIndex(lines, line => line.Trim() == heading);
        headingIndex.ShouldBeGreaterThanOrEqualTo(0, $"CHANGELOG.md must contain a '{heading}' heading line.");

        var section = lines
            .Skip(headingIndex + 1)
            .TakeWhile(line => !Regex.IsMatch(line, @"^\s*##\s"));

        string.Join('\n', section).Trim()
            .ShouldNotBeEmpty($"CHANGELOG.md section '{heading}' must not be empty.");
    }

    private static void AssertReadmeIsPacked(
        XDocument project,
        string projectDirectory,
        string readmeFile,
        string packageId)
    {
        var packedReadme = project
            .Descendants()
            .Where(element => element.Name.LocalName == "None")
            .Select(element => new
            {
                Include = element.Attribute("Include")?.Value,
                Pack = element.Attribute("Pack")?.Value,
                PackagePath = element.Attribute("PackagePath")?.Value
            })
            .Where(item => string.Equals(item.Pack, "true", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault(item =>
            {
                if (string.IsNullOrWhiteSpace(item.Include))
                    return false;

                return string.Equals(
                    Path.GetFileName(NormalizePath(item.Include)),
                    readmeFile,
                    StringComparison.Ordinal);
            });

        packedReadme.ShouldNotBeNull($"{packageId} must pack {readmeFile}.");
        NormalizePath(packedReadme.Include!).ShouldBe(
            readmeFile,
            $"{packageId} must pack a package-local {readmeFile}, not a shared repository README.");
        packedReadme.PackagePath.ShouldBe("\\");

        var readmePath = Path.GetFullPath(Path.Combine(projectDirectory, NormalizePath(packedReadme.Include!)));
        File.Exists(readmePath).ShouldBeTrue($"{packageId} readme file does not exist at {readmePath}");
    }

    private static string ReadRequiredProperty(XDocument project, string name, string packageId)
    {
        var value = project
            .Descendants()
            .Where(element => element.Name.LocalName == name)
            .Select(element => element.Value.Trim())
            .FirstOrDefault(value => value.Length > 0);

        string.IsNullOrWhiteSpace(value).ShouldBeFalse($"{packageId} must define {name}.");
        return value!;
    }

    private static string? ReadOptionalProperty(XDocument project, string name)
        => project
            .Descendants()
            .Where(element => element.Name.LocalName == name)
            .Select(element => element.Value.Trim())
            .FirstOrDefault(value => value.Length > 0);

    private static void AssertUnique(IEnumerable<string> values, string label)
    {
        var duplicates = values
            .GroupBy(value => value, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        duplicates.ShouldBeEmpty($"{label} values must be unique.");
    }

    private static string NormalizePath(string path)
        => path
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);

    private static string NormalizeManifestPath(string path)
        => path.Replace('\\', '/');
}
