using System.Xml.Linq;
using Shouldly;
using Xunit;

namespace FluxFlow.Release.Tests;

public sealed class PackageProjectConventionTests
{
    private static readonly string[] ExpectedTargetFrameworks = ["net8.0", "net10.0"];

    [Fact]
    public void Package_projects_use_release_packaging_conventions()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();

        foreach (var entry in PackageManifest.Read(root))
        {
            var projectPath = Path.GetFullPath(Path.Combine(root, NormalizePath(entry.Project)));
            var project = XDocument.Load(projectPath);
            var metadata = PackageProjectMetadata.Read(project, entry.PackageId);

            metadata.PackageId.ShouldBe(entry.PackageId);
            metadata.TargetFrameworks.ShouldBe(ExpectedTargetFrameworks);
            metadata.AssemblyName.ShouldBe(entry.PackageId);
            metadata.RootNamespace.ShouldBe(entry.PackageId);
            metadata.Authors.ShouldBe("FluxFlow contributors");
            string.IsNullOrWhiteSpace(metadata.Description).ShouldBeFalse($"{entry.PackageId} must define a description.");
            metadata.PackageTags.ShouldContain("workflow");
            metadata.PackageTags.ShouldContain("dataflow");
            if (entry.PackageId.StartsWith("FluxFlow.Components.", StringComparison.Ordinal))
                metadata.PackageTags.ShouldContain("components");

            metadata.PackageLicenseExpression.ShouldBe("MIT");
            metadata.PackageReadmeFile.ShouldBe("README.md");
            string.Equals(metadata.IncludeSymbols, "true", StringComparison.OrdinalIgnoreCase)
                .ShouldBeTrue($"{entry.PackageId} must include symbols.");
            metadata.SymbolPackageFormat.ShouldBe("snupkg");
            string.IsNullOrWhiteSpace(metadata.PackageReleaseNotes).ShouldBeFalse($"{entry.PackageId} must define release notes.");
        }
    }

    [Fact]
    public void Package_project_references_target_manifested_packages()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var entries = PackageManifest.Read(root);
        var packageIds = entries
            .Select(entry => entry.PackageId)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var entry in entries)
        {
            var projectPath = Path.GetFullPath(Path.Combine(root, NormalizePath(entry.Project)));
            var projectDirectory = Path.GetDirectoryName(projectPath).ShouldNotBeNull();
            var project = XDocument.Load(projectPath);

            foreach (var reference in ReadProjectReferences(project))
            {
                var referencePath = Path.GetFullPath(Path.Combine(projectDirectory, NormalizePath(reference)));
                File.Exists(referencePath).ShouldBeTrue($"{entry.PackageId} references missing project {reference}.");

                var referencedProject = XDocument.Load(referencePath);
                var referencedPackageId = ReadOptionalProperty(referencedProject, "PackageId");

                string.IsNullOrWhiteSpace(referencedPackageId)
                    .ShouldBeFalse($"{entry.PackageId} references {reference}, but that project has no package id.");
                packageIds.Contains(referencedPackageId!)
                    .ShouldBeTrue($"{entry.PackageId} references {referencedPackageId}, but it is not in the package manifest.");
            }
        }
    }

    private static IEnumerable<string> ReadProjectReferences(XDocument project)
        => project
            .Descendants()
            .Where(element => element.Name.LocalName == "ProjectReference")
            .Select(element => element.Attribute("Include")?.Value.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))!;

    private static string? ReadOptionalProperty(XDocument project, string name)
        => project
            .Descendants()
            .Where(element => element.Name.LocalName == name)
            .Select(element => element.Value.Trim())
            .FirstOrDefault(value => value.Length > 0);

    private static string ReadRequiredProperty(XDocument project, string name, string packageId)
    {
        var value = ReadOptionalProperty(project, name);
        string.IsNullOrWhiteSpace(value).ShouldBeFalse($"{packageId} must define {name}.");
        return value!;
    }

    private static IReadOnlyList<string> ReadTargetFrameworks(XDocument project, string packageId)
    {
        var targetFrameworks = ReadOptionalProperty(project, "TargetFrameworks");
        if (!string.IsNullOrWhiteSpace(targetFrameworks))
            return SplitList(targetFrameworks);

        return [ReadRequiredProperty(project, "TargetFramework", packageId)];
    }

    private static IReadOnlyList<string> SplitList(string value)
        => value
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(item => item.Length > 0)
            .ToArray();

    private static string NormalizePath(string path)
        => path
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);

    private sealed record PackageProjectMetadata(
        string PackageId,
        IReadOnlyList<string> TargetFrameworks,
        string AssemblyName,
        string RootNamespace,
        string Authors,
        string Description,
        IReadOnlyList<string> PackageTags,
        string PackageLicenseExpression,
        string PackageReadmeFile,
        string IncludeSymbols,
        string SymbolPackageFormat,
        string PackageReleaseNotes)
    {
        public static PackageProjectMetadata Read(XDocument project, string packageId)
            => new(
                ReadRequiredProperty(project, "PackageId", packageId),
                ReadTargetFrameworks(project, packageId),
                ReadRequiredProperty(project, "AssemblyName", packageId),
                ReadRequiredProperty(project, "RootNamespace", packageId),
                ReadRequiredProperty(project, "Authors", packageId),
                ReadRequiredProperty(project, "Description", packageId),
                SplitList(ReadRequiredProperty(project, "PackageTags", packageId)),
                ReadRequiredProperty(project, "PackageLicenseExpression", packageId),
                ReadRequiredProperty(project, "PackageReadmeFile", packageId),
                ReadRequiredProperty(project, "IncludeSymbols", packageId),
                ReadRequiredProperty(project, "SymbolPackageFormat", packageId),
                ReadRequiredProperty(project, "PackageReleaseNotes", packageId));
    }
}
