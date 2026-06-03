using System.Text.Json;
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
        var root = FindRepositoryRoot();
        var entries = ReadPackageEntries(root);
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

    private static IReadOnlyList<PackageManifestEntry> ReadPackageEntries(string root)
    {
        var manifestPath = Path.Combine(root, "eng", "packages.json");
        var entries = JsonSerializer.Deserialize<IReadOnlyList<PackageManifestEntry>>(
            File.ReadAllText(manifestPath),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        entries.ShouldNotBeNull();
        return entries;
    }

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

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "eng", "packages.json")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find the repository root.");
    }

    private sealed record PackageManifestEntry
    {
        public required string Alias { get; init; }
        public required string TagPrefix { get; init; }
        public required string PackageId { get; init; }
        public required string Project { get; init; }
        public required string NotesName { get; init; }
    }
}
