using System.Xml.Linq;
using Shouldly;
using Xunit;

namespace FluxFlow.Release.Tests;

public sealed class DataFoundationBoundaryTests
{
    [Fact]
    public void Data_package_stays_transport_neutral_and_dependency_free()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var entry = PackageManifest
            .Read(root)
            .Single(item => string.Equals(item.Alias, "data", StringComparison.Ordinal));
        var projectPath = Path.GetFullPath(Path.Combine(root, NormalizePath(entry.Project)));
        var project = XDocument.Load(projectPath);

        project.Descendants("ProjectReference").ShouldBeEmpty(
            "FluxFlow.Data must not depend on another FluxFlow package.");
        project.Descendants("PackageReference").ShouldBeEmpty(
            "FluxFlow.Data must remain free of runtime package dependencies.");

        var projectDirectory = Path.GetDirectoryName(projectPath).ShouldNotBeNull();
        var source = string.Join(
            '\n',
            Directory
                .EnumerateFiles(projectDirectory, "*.cs", SearchOption.TopDirectoryOnly)
                .Order(StringComparer.Ordinal)
                .Select(File.ReadAllText));

        source.ShouldNotContain("System.Threading.Tasks.Dataflow", Case.Insensitive);
        source.ShouldNotContain("FluxFlow.Nodes", Case.Insensitive);
        source.ShouldNotContain("FluxFlow.Composition", Case.Insensitive);
        source.ShouldNotContain("FluxFlow.Engine", Case.Insensitive);
    }

    private static string NormalizePath(string path)
        => path
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);
}
