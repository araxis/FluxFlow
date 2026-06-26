using System.Xml.Linq;
using Shouldly;
using Xunit;

namespace FluxFlow.Release.Tests;

public sealed class ComponentPackageBoundaryTests
{
    private static readonly string[] ForbiddenNonCompositionReferences =
    [
        "FluxFlow.Engine",
        "FluxFlow.Composition",
        "FluxFlow.Composition.Hosting",
        "FluxFlow.Components.Designer"
    ];

    private static readonly string[] ForbiddenNonCompositionFilePatterns =
    [
        "*CompositionNodeRegistryExtensions.cs",
        "*CompositionNodeTypes.cs",
        "*CompositionPortNames.cs",
        "*CompositionResourceNames.cs",
        "*ComponentDesignMetadataProvider.cs"
    ];

    private static readonly string[] SupportOnlyComponentPackageIds =
    [
        "FluxFlow.Components.Configuration",
        "FluxFlow.Components.Expressions",
        "FluxFlow.Components.Journal",
        "FluxFlow.Components.Resources",
        "FluxFlow.Components.Secrets",
        "FluxFlow.Components.Storage.FileSystem",
        "FluxFlow.Components.Storage.SqlFile"
    ];

    private static readonly string[] ForbiddenSupportOnlyReferences =
    [
        "FluxFlow.Nodes",
        "FluxFlow.Engine",
        "FluxFlow.Composition",
        "FluxFlow.Composition.Hosting",
        "FluxFlow.Components.Designer"
    ];

    [Fact]
    public void Non_composition_component_packages_stay_free_of_composition_designer_and_engine_references()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();

        foreach (var entry in ReadNonCompositionComponentPackages(root))
        {
            var project = XDocument.Load(ReadProjectPath(root, entry));
            var projectDirectory = ReadProjectDirectory(root, entry);
            var referencedPackageIds = ReadAllReferencedPackageIds(project, projectDirectory)
                .ToArray();

            foreach (var forbiddenReference in ForbiddenNonCompositionReferences)
            {
                referencedPackageIds.ShouldNotContain(
                    forbiddenReference,
                    $"{entry.PackageId} must keep composition, Designer, and engine dependencies in optional adapter packages.");
            }
        }
    }

    [Fact]
    public void Support_only_component_packages_stay_free_of_node_runtime_references()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var entriesByPackageId = PackageManifest
            .Read(root)
            .ToDictionary(entry => entry.PackageId, StringComparer.Ordinal);

        foreach (var packageId in SupportOnlyComponentPackageIds)
        {
            entriesByPackageId.ContainsKey(packageId)
                .ShouldBeTrue($"{packageId} must stay listed in the package manifest.");
        }

        foreach (var packageId in SupportOnlyComponentPackageIds.Order(StringComparer.Ordinal))
        {
            var entry = entriesByPackageId[packageId];
            var project = XDocument.Load(ReadProjectPath(root, entry));
            var projectDirectory = ReadProjectDirectory(root, entry);
            var referencedPackageIds = ReadAllReferencedPackageIds(project, projectDirectory)
                .ToArray();

            foreach (var forbiddenReference in ForbiddenSupportOnlyReferences)
            {
                referencedPackageIds.ShouldNotContain(
                    forbiddenReference,
                    $"{entry.PackageId} must stay a support package; node runtimes belong in normal component packages or optional adapters.");
            }

            var nodeFiles = Directory
                .EnumerateFiles(projectDirectory, "*Node.cs", SearchOption.AllDirectories)
                .Select(file => Path.GetRelativePath(projectDirectory, file))
                .Order(StringComparer.Ordinal)
                .ToArray();
            var nodeDirectories = Directory
                .EnumerateDirectories(projectDirectory, "Nodes", SearchOption.AllDirectories)
                .Select(directory => Path.GetRelativePath(projectDirectory, directory))
                .Order(StringComparer.Ordinal)
                .ToArray();

            nodeFiles.ShouldBeEmpty($"{entry.PackageId} must not ship node classes.");
            nodeDirectories.ShouldBeEmpty($"{entry.PackageId} must not ship a Nodes folder.");
        }
    }

    [Fact]
    public void Non_composition_component_packages_do_not_ship_composition_or_designer_artifacts()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();

        foreach (var entry in ReadNonCompositionComponentPackages(root))
        {
            var projectDirectory = ReadProjectDirectory(root, entry);

            foreach (var pattern in ForbiddenNonCompositionFilePatterns)
            {
                var files = Directory
                    .EnumerateFiles(projectDirectory, pattern, SearchOption.TopDirectoryOnly)
                    .Select(file => Path.GetFileName(file))
                    .ToArray();

                files.ShouldBeEmpty(
                    $"{entry.PackageId} must keep composition factories and Designer metadata in a separate optional package.");
            }
        }
    }

    [Fact]
    public void Non_composition_component_readmes_document_their_composition_boundary()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var packageIds = PackageManifest
            .Read(root)
            .Select(entry => entry.PackageId)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var entry in ReadNonCompositionComponentPackages(root))
        {
            var readmePath = Path.Combine(ReadProjectDirectory(root, entry), "README.md");
            File.Exists(readmePath)
                .ShouldBeTrue($"{entry.PackageId} must include a package README.");

            var readme = File.ReadAllText(readmePath);
            readme.Contains("## Composition", StringComparison.Ordinal)
                .ShouldBeTrue($"{entry.PackageId} README must document its composition boundary.");

            var adapterPackageId = entry.PackageId + ".Composition";
            if (packageIds.Contains(adapterPackageId))
            {
                readme.Contains(adapterPackageId, StringComparison.Ordinal)
                    .ShouldBeTrue(
                        $"{entry.PackageId} README must point composition users to {adapterPackageId}.");
                continue;
            }

            readme.Contains("does not expose", StringComparison.OrdinalIgnoreCase)
                .ShouldBeTrue(
                    $"{entry.PackageId} README must explicitly state that it does not expose composition factories.");
            readme.Contains("FluxFlow.Composition", StringComparison.Ordinal)
                .ShouldBeTrue(
                    $"{entry.PackageId} README must name FluxFlow.Composition when documenting the absence of composition factories.");
        }
    }

    private static PackageManifestEntry[] ReadNonCompositionComponentPackages(string root)
        => PackageManifest
            .Read(root)
            .Where(IsNonCompositionComponentPackage)
            .OrderBy(entry => entry.PackageId, StringComparer.Ordinal)
            .ToArray();

    private static bool IsNonCompositionComponentPackage(PackageManifestEntry entry)
        => entry.PackageId.StartsWith("FluxFlow.Components.", StringComparison.Ordinal) &&
            !entry.PackageId.EndsWith(".Composition", StringComparison.Ordinal) &&
            !string.Equals(entry.PackageId, "FluxFlow.Components.Designer", StringComparison.Ordinal);

    private static string ReadProjectPath(
        string root,
        PackageManifestEntry entry)
        => Path.GetFullPath(Path.Combine(root, NormalizePath(entry.Project)));

    private static string ReadProjectDirectory(
        string root,
        PackageManifestEntry entry)
        => Path.GetDirectoryName(ReadProjectPath(root, entry)).ShouldNotBeNull();

    private static IEnumerable<string> ReadReferencedPackageIds(
        XDocument project,
        string projectDirectory)
    {
        foreach (var reference in project.Descendants("ProjectReference"))
        {
            var include = reference.Attribute("Include")?.Value;
            include.ShouldNotBeNullOrWhiteSpace();

            var referencedProjectPath = Path.GetFullPath(Path.Combine(projectDirectory, include!));
            var referencedProject = XDocument.Load(referencedProjectPath);
            yield return ReadRequiredProperty(
                referencedProject,
                "PackageId",
                referencedProjectPath);
        }
    }

    private static IEnumerable<string> ReadAllReferencedPackageIds(
        XDocument project,
        string projectDirectory)
        => ReadReferencedPackageIds(project, projectDirectory)
            .Concat(ReadPackageReferenceIds(project));

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
        => path.Replace('/', Path.DirectorySeparatorChar);
}
