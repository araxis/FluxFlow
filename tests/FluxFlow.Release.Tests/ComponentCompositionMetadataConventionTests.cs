using System.Xml.Linq;
using Shouldly;
using Xunit;

namespace FluxFlow.Release.Tests;

public sealed class ComponentCompositionMetadataConventionTests
{
    [Fact]
    public void Component_composition_packages_ship_designer_metadata_providers()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var entries = PackageManifest
            .Read(root)
            .Where(IsComponentCompositionPackage)
            .OrderBy(entry => entry.PackageId, StringComparer.Ordinal)
            .ToArray();

        entries.ShouldNotBeEmpty("component composition packages should be listed in the release manifest.");

        foreach (var entry in entries)
        {
            var projectPath = Path.GetFullPath(Path.Combine(root, NormalizePath(entry.Project)));
            var projectDirectory = Path.GetDirectoryName(projectPath).ShouldNotBeNull();
            var providerFiles = Directory
                .EnumerateFiles(
                    projectDirectory,
                    "*ComponentDesignMetadataProvider.cs",
                    SearchOption.TopDirectoryOnly)
                .Order(StringComparer.Ordinal)
                .ToArray();

            providerFiles.Length.ShouldBe(
                1,
                $"{entry.PackageId} must ship exactly one package-owned Designer metadata provider.");

            var providerContent = File.ReadAllText(providerFiles[0]);
            providerContent.Contains(
                    "IComponentDesignMetadataProvider",
                    StringComparison.Ordinal)
                .ShouldBeTrue($"{entry.PackageId} provider must implement IComponentDesignMetadataProvider.");

            var project = XDocument.Load(projectPath);
            var referencedPackageIds = ReadReferencedPackageIds(project, projectDirectory)
                .ToArray();

            referencedPackageIds.ShouldContain(
                "FluxFlow.Components.Designer",
                $"{entry.PackageId} must reference Designer for its metadata provider.");
            referencedPackageIds.ShouldNotContain(
                "FluxFlow.Engine",
                $"{entry.PackageId} must stay engine-free.");
        }
    }

    private static bool IsComponentCompositionPackage(PackageManifestEntry entry)
        => entry.PackageId.StartsWith("FluxFlow.Components.", StringComparison.Ordinal)
            && entry.PackageId.EndsWith(".Composition", StringComparison.Ordinal);

    private static IEnumerable<string> ReadReferencedPackageIds(
        XDocument project,
        string projectDirectory)
    {
        foreach (var reference in project
            .Descendants()
            .Where(element => element.Name.LocalName == "ProjectReference")
            .Select(element => element.Attribute("Include")?.Value.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            var referencePath = Path.GetFullPath(
                Path.Combine(projectDirectory, NormalizePath(reference!)));
            var referencedProject = XDocument.Load(referencePath);
            var packageId = referencedProject
                .Descendants()
                .Where(element => element.Name.LocalName == "PackageId")
                .Select(element => element.Value.Trim())
                .FirstOrDefault(value => value.Length > 0);

            if (!string.IsNullOrWhiteSpace(packageId))
                yield return packageId;
        }
    }

    private static string NormalizePath(string path)
        => path
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);
}
