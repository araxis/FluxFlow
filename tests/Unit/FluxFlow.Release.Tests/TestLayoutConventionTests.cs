using Shouldly;
using Xunit;

namespace FluxFlow.Release.Tests;

public sealed class TestLayoutConventionTests
{
    [Fact]
    public void Test_projects_live_directly_under_their_test_kind()
    {
        var repositoryRoot = FindRepositoryRoot();
        var testsRoot = Path.Combine(repositoryRoot, "tests");
        var expectedRoots = new[] { "Acceptance", "Integration", "Smoke", "Support", "Unit" };
        Directory.GetDirectories(testsRoot)
            .Select(static path => Path.GetFileName(path)!)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ShouldAllBe(name => expectedRoots.Contains(name));

        var projects = Directory.GetFiles(testsRoot, "*.csproj", SearchOption.AllDirectories);
        projects.ShouldNotBeEmpty();
        foreach (var project in projects)
        {
            var relativePath = Path.GetRelativePath(testsRoot, project);
            var segments = relativePath.Split(Path.DirectorySeparatorChar);
            segments.Length.ShouldBe(3, relativePath);
            var projectName = Path.GetFileNameWithoutExtension(project);
            projectName.ShouldBe(Path.GetFileName(Path.GetDirectoryName(project)), relativePath);

            switch (segments[0])
            {
                case "Unit":
                    projectName.EndsWith(".Tests", StringComparison.Ordinal).ShouldBeTrue(relativePath);
                    break;
                case "Integration":
                    projectName.EndsWith("IntegrationTests", StringComparison.Ordinal).ShouldBeTrue(relativePath);
                    break;
                case "Acceptance":
                    projectName.EndsWith(".Acceptance.Tests", StringComparison.Ordinal).ShouldBeTrue(relativePath);
                    break;
                case "Smoke":
                    projectName.EndsWith("SmokeTests", StringComparison.Ordinal).ShouldBeTrue(relativePath);
                    break;
                case "Support":
                    projectName.ShouldBe("FluxFlow.Testing", relativePath);
                    break;
                default:
                    throw new ShouldAssertException($"Unexpected test root in {relativePath}.");
            }
        }
    }

    [Fact]
    public void Production_projects_follow_framework_and_component_module_layout()
    {
        var root = FindRepositoryRoot();
        var sourceRoot = Path.Combine(root, "src");
        var solution = File.ReadAllText(Path.Combine(root, "FluxFlow.sln"));
        var projects = Directory.GetFiles(sourceRoot, "*.csproj", SearchOption.AllDirectories);
        projects.ShouldNotBeEmpty();
        solution.ShouldContain("\"Components\", \"Components\"");
        foreach (var project in projects)
        {
            var relative = Path.GetRelativePath(sourceRoot, project);
            var segments = relative.Split(Path.DirectorySeparatorChar);
            if (segments[0] == "Components")
            {
                segments.Length.ShouldBe(3, relative);
                segments[1].ShouldStartWith("FluxFlow.Components.");
            }
            else if (segments[0] == "Expressions")
            {
                segments.Length.ShouldBe(3, relative);
                segments[1].ShouldStartWith("FluxFlow.Expressions.");
                solution.ShouldContain("\"Expressions\", \"Expressions\"");
            }
            else
            {
                segments.Length.ShouldBe(2, relative);
                new[] { "FluxFlow.Core", "FluxFlow.Configuration" }.ShouldContain(segments[0]);
            }
            Path.GetFileNameWithoutExtension(project).ShouldBe(Path.GetFileName(Path.GetDirectoryName(project)));
            solution.ShouldContain($"\"{Path.GetRelativePath(root, project).Replace(Path.DirectorySeparatorChar, '\\')}\"");
        }
    }

    [Fact]
    public void Solution_contains_every_test_project_at_its_physical_path()
    {
        var repositoryRoot = FindRepositoryRoot();
        var testsRoot = Path.Combine(repositoryRoot, "tests");
        var solution = File.ReadAllText(Path.Combine(repositoryRoot, "FluxFlow.sln"));

        foreach (var project in Directory.GetFiles(testsRoot, "*.csproj", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(repositoryRoot, project)
                .Replace(Path.DirectorySeparatorChar, '\\');
            solution.ShouldContain($"\"{relativePath}\"");
        }
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FluxFlow.sln")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
