using Shouldly;
using Xunit;

namespace FluxFlow.Release.Tests;

public sealed class LinkOwnershipBoundaryTests
{
    [Fact]
    public void Composition_has_no_production_friend_access_for_designer_or_engine()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var project = File.ReadAllText(Path.Combine(
            root,
            "src",
            "FluxFlow.Composition",
            "FluxFlow.Composition.csproj"));

        project.ShouldNotContain("InternalsVisibleTo Include=\"FluxFlow.Components.Designer\"");
        project.ShouldNotContain("InternalsVisibleTo Include=\"FluxFlow.Engine\"");
    }

    [Fact]
    public void Designer_and_engine_use_owned_public_boundaries()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var designer = ReadSources(Path.Combine(root, "src", "FluxFlow.Components.Designer"));
        var engine = ReadSources(Path.Combine(root, "src", "FluxFlow.Engine"));

        designer.ShouldNotContain("ApplicationLinkDeclarationParser");
        designer.ShouldNotContain("CanonicalApplicationProperties");
        engine.ShouldNotContain("CanonicalApplicationProperties");
        File.Exists(Path.Combine(
            root,
            "src",
            "FluxFlow.Composition",
            "Configuration",
            "ConfigurationJsonReader.cs")).ShouldBeFalse();
        File.Exists(Path.Combine(
            root,
            "src",
            "FluxFlow.Engine",
            "Internal",
            "ConfigurationJsonReader.cs")).ShouldBeTrue();
    }

    private static string ReadSources(string directory)
        => string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
                .Where(path => !path.Contains(
                    $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal))
                .OrderBy(static path => path, StringComparer.Ordinal)
                .Select(File.ReadAllText));
}
