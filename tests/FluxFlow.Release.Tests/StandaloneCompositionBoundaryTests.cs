using System.Xml.Linq;
using Shouldly;
using Xunit;

namespace FluxFlow.Release.Tests;

public sealed class StandaloneCompositionBoundaryTests
{
    private static readonly string[] StandaloneCompositionPackageIds =
    [
        "FluxFlow.Composition",
        "FluxFlow.Composition.Hosting"
    ];

    private static readonly string[] ForbiddenStandaloneCompositionReferences =
    [
        "FluxFlow.Engine",
        "FluxFlow.Components.Designer"
    ];

    [Fact]
    public void Standalone_composition_packages_stay_engine_and_designer_free()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var entries = PackageManifest
            .Read(root)
            .ToDictionary(entry => entry.PackageId, StringComparer.Ordinal);

        foreach (var packageId in StandaloneCompositionPackageIds)
        {
            entries.ContainsKey(packageId)
                .ShouldBeTrue($"{packageId} must stay listed in the package manifest.");

            var entry = entries[packageId];
            var projectPath = Path.GetFullPath(Path.Combine(root, NormalizePath(entry.Project)));
            var projectDirectory = Path.GetDirectoryName(projectPath).ShouldNotBeNull();
            var project = XDocument.Load(projectPath);
            var referencedPackageIds = ReadAllReferencedPackageIds(project, projectDirectory)
                .ToArray();

            foreach (var forbiddenReference in ForbiddenStandaloneCompositionReferences)
            {
                referencedPackageIds.ShouldNotContain(
                    forbiddenReference,
                    $"{packageId} must stay standalone-first and must not depend on {forbiddenReference}.");
            }
        }
    }

    [Fact]
    public void Composition_core_does_not_reference_hosting_bridge()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var entries = PackageManifest
            .Read(root)
            .ToDictionary(entry => entry.PackageId, StringComparer.Ordinal);
        var entry = entries["FluxFlow.Composition"];
        var projectPath = Path.GetFullPath(Path.Combine(root, NormalizePath(entry.Project)));
        var projectDirectory = Path.GetDirectoryName(projectPath).ShouldNotBeNull();
        var project = XDocument.Load(projectPath);

        var referencedPackageIds = ReadAllReferencedPackageIds(project, projectDirectory)
            .ToArray();

        referencedPackageIds.ShouldNotContain(
            "FluxFlow.Composition.Hosting",
            "FluxFlow.Composition must stay independent from the optional hosting bridge.");
    }

    private static IEnumerable<string> ReadAllReferencedPackageIds(
        XDocument project,
        string projectDirectory)
        => ReadReferencedPackageIds(project, projectDirectory)
            .Concat(ReadPackageReferenceIds(project));

    private static IEnumerable<string> ReadReferencedPackageIds(
        XDocument project,
        string projectDirectory)
    {
        foreach (var reference in project.Descendants("ProjectReference"))
        {
            var include = reference.Attribute("Include")?.Value;
            include.ShouldNotBeNullOrWhiteSpace();

            var referencedProjectPath = Path.GetFullPath(Path.Combine(projectDirectory, NormalizePath(include!)));
            var referencedProject = XDocument.Load(referencedProjectPath);
            yield return ReadRequiredProperty(
                referencedProject,
                "PackageId",
                referencedProjectPath);
        }
    }

    private static IEnumerable<string> ReadPackageReferenceIds(XDocument project)
        => project
            .Descendants("PackageReference")
            .Select(reference => reference.Attribute("Include")?.Value.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))!;

    private static string ReadRequiredProperty(
        XDocument project,
        string propertyName,
        string subject)
    {
        var value = project
            .Descendants(propertyName)
            .Select(element => element.Value)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        value.ShouldNotBeNullOrWhiteSpace($"{subject} must define {propertyName}.");
        return value!;
    }

    private static string NormalizePath(string path)
        => path
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);
}
