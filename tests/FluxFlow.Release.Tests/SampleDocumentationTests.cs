using System.Text.RegularExpressions;
using Shouldly;
using Xunit;

namespace FluxFlow.Release.Tests;

public sealed partial class SampleDocumentationTests
{
    [Fact]
    public void Sample_projects_are_listed_in_solution_and_docs_inventory()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var solution = File.ReadAllText(Path.Combine(root, "FluxFlow.sln"));
        var docsReadme = File.ReadAllText(Path.Combine(root, "docs", "README.md"));
        var sampleProjects = Directory
            .EnumerateFiles(Path.Combine(root, "samples"), "*.csproj", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToArray();

        sampleProjects.ShouldNotBeEmpty("the repository should keep at least one sample project.");

        foreach (var project in sampleProjects)
        {
            var sampleDirectory = Path.GetDirectoryName(project)!.Replace('\\', '/');

            solution.Contains(project.Replace('/', '\\'), StringComparison.Ordinal)
                .ShouldBeTrue($"{project} must be included in FluxFlow.sln.");
            docsReadme.Contains(sampleDirectory, StringComparison.Ordinal)
                .ShouldBeTrue($"docs/README.md must list {sampleDirectory} in the sample inventory.");
        }
    }

    [Fact]
    public void Documented_sample_run_commands_point_to_existing_projects()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var documents = Directory
            .EnumerateFiles(root, "*.md", SearchOption.AllDirectories)
            .Where(path => !IsGeneratedOrBuildOutput(root, path))
            .Order(StringComparer.Ordinal)
            .ToArray();
        var missingProjects = new List<string>();

        foreach (var document in documents)
        {
            var relativeDocument = Path.GetRelativePath(root, document).Replace('\\', '/');
            foreach (Match match in SampleRunProjectRegex().Matches(File.ReadAllText(document)))
            {
                var project = match.Groups["project"].Value.Replace('\\', '/');
                var projectPath = Path.Combine(root, project.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(projectPath))
                {
                    missingProjects.Add($"{relativeDocument}: {project}");
                }
            }
        }

        missingProjects.ShouldBeEmpty("documented sample run commands must target existing sample projects.");
    }

    private static bool IsGeneratedOrBuildOutput(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
        return relative.Contains("/bin/", StringComparison.Ordinal) ||
               relative.Contains("/obj/", StringComparison.Ordinal);
    }

    [GeneratedRegex(@"dotnet\s+run\s+--project\s+(?<project>samples[\\/][^\s`)]+\.csproj)")]
    private static partial Regex SampleRunProjectRegex();
}
