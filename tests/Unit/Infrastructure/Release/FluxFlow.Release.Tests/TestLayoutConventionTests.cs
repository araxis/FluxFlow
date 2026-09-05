using Shouldly;
using Xunit;

namespace FluxFlow.Release.Tests;

public sealed class TestLayoutConventionTests
{
    [Fact]
    public void Test_projects_follow_the_declared_four_kind_layout()
    {
        var repositoryRoot = FindRepositoryRoot();
        var testsRoot = Path.Combine(repositoryRoot, "tests");
        var expectedRoots = new[] { "Acceptance", "Integration", "Smoke", "Support", "Unit" };
        Directory.GetDirectories(testsRoot)
            .Select(static path => Path.GetFileName(path)!)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ShouldBe(expectedRoots);

        var projects = Directory.GetFiles(testsRoot, "*.csproj", SearchOption.AllDirectories);
        projects.ShouldNotBeEmpty();
        foreach (var project in projects)
        {
            var relativePath = Path.GetRelativePath(testsRoot, project);
            var segments = relativePath.Split(Path.DirectorySeparatorChar);
            segments.Length.ShouldBeGreaterThanOrEqualTo(3, relativePath);
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
