using System.Text.RegularExpressions;
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
                    (projectName.EndsWith(".Tests", StringComparison.Ordinal) ||
                        projectName.EndsWith(".IntegrationTests", StringComparison.Ordinal)).ShouldBeTrue(relativePath);
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
        var declarations = Regex.Matches(solution,
            """^Project\("[^"]+"\) = "([^"]+)", "([^"]+)", "(\{[^}]+\})"\r?$""", RegexOptions.Multiline);
        var byPath = declarations.Where(m => m.Groups[2].Value.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(m => m.Groups[2].Value, m => m.Groups[3].Value);
        var names = declarations.ToDictionary(m => m.Groups[3].Value, m => m.Groups[1].Value);
        var parents = Regex.Matches(solution, @"(?m)^\s*(\{[\w-]+\}) = (\{[\w-]+\})\s*$")
            .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);
        var plugins = names.Single(pair => pair.Value == "Plugins").Key;
        names[parents[plugins]].ShouldBe("src");
        foreach (var project in projects)
        {
            var relative = Path.GetRelativePath(sourceRoot, project);
            var segments = relative.Split(Path.DirectorySeparatorChar);
            var solutionPath = Path.GetRelativePath(root, project).Replace(Path.DirectorySeparatorChar, '\\');
            byPath.ShouldContainKey(solutionPath);
            if (segments[0] == "Plugins")
            {
                var folder = parents[byPath[solutionPath]];
                if (segments[1] == "FluxFlow.Plugins")
                    folder.ShouldBe(plugins, relative);
                else
                {
                    parents[folder].ShouldBe(plugins, relative);
                    segments[1].ShouldStartWith("FluxFlow.Plugins.");
                    names[folder].Replace(" ", "").Equals(segments[1]["FluxFlow.Plugins.".Length..],
                        StringComparison.OrdinalIgnoreCase).ShouldBeTrue(relative);
                }
                continue;
            }
            else if (segments[0] == "Expressions")
            {
                segments.Length.ShouldBe(3, relative);
                segments[1].ShouldStartWith("FluxFlow.Expressions.");
                solution.ShouldContain("\"Expressions\", \"Expressions\"");
            }
            else if (segments[0] == "Studio")
            {
                segments.Length.ShouldBe(3, relative);
                new[] { "FluxFlow.DesignerApp", "FluxFlow.Studio.UI", "FluxFlow.StudioWebHost", "FluxFlow.Studio.Desktop" }.ShouldContain(segments[1]);
                solution.ShouldContain("\"Studio\", \"Studio\"");
            }
            else if (segments[0] == "Authoring")
            {
                segments.Length.ShouldBe(3, relative);
                new[] { "FluxFlow.Authoring", "FluxFlow.Conversations", "FluxFlow.Conversations.Local" }.ShouldContain(segments[1]);
                solution.ShouldContain("\"Authoring\", \"Authoring\"");
            }
            else if (segments[0] == "Hosting")
            {
                segments.Length.ShouldBe(3, relative);
                new[] { "FluxFlow.Execution", "FluxFlow.Runner" }.ShouldContain(segments[1]);
                solution.ShouldContain("\"Hosting\", \"Hosting\"");
            }
            else if (segments[0] == "Environments")
            {
                segments.Length.ShouldBe(3, relative);
                segments[1].ShouldStartWith("FluxFlow.Environments");
            }
            else
            {
                segments.Length.ShouldBe(2, relative);
                new[] { "FluxFlow.Core", "FluxFlow.Configuration", "FluxFlow.Application" }.ShouldContain(segments[0]);
            }
            Path.GetFileNameWithoutExtension(project).ShouldBe(Path.GetFileName(Path.GetDirectoryName(project)));
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

    [Fact]
    public void Authoring_projects_have_no_transitive_Studio_or_rendering_dependency()
    {
        var source = Path.Combine(FindRepositoryRoot(), "src");
        var pending = new Stack<string>(Directory.GetFiles(Path.Combine(source, "Authoring"), "*.csproj", SearchOption.AllDirectories));
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (pending.TryPop(out var path))
        {
            path = Path.GetFullPath(path);
            if (!visited.Add(path)) continue;
            path.StartsWith(Path.Combine(source, "Studio") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase).ShouldBeFalse(path);
            var project = System.Xml.Linq.XDocument.Load(path);
            var sdk = project.Root!.Attribute("Sdk")?.Value ?? "";
            sdk.Contains("Razor", StringComparison.Ordinal).ShouldBeFalse(path);
            foreach (var package in project.Descendants("PackageReference"))
            {
                var name = package.Attribute("Include")!.Value;
                name.StartsWith("Microsoft.AspNetCore.Components", StringComparison.Ordinal).ShouldBeFalse(path);
                new[] { "MudBlazor", "BlazorMonaco", "Z.Blazor.Diagrams" }.ShouldNotContain(name, path);
            }
            foreach (var reference in project.Descendants("ProjectReference"))
                pending.Push(Path.Combine(Path.GetDirectoryName(path)!, reference.Attribute("Include")!.Value));
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
