using System.Xml.Linq;
using Shouldly;
using Xunit;

namespace FluxFlow.Release.Tests;

public sealed class RuntimePackageBoundaryTests
{
    [Fact]
    public void Runtime_distribution_has_no_editor_projects_or_ui_dependencies()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var entries = PackageManifest.Read(root);
        entries.ShouldNotBeEmpty();
        foreach (var entry in entries)
        {
            IsUi(entry.PackageId).ShouldBeFalse(entry.PackageId);
            var projectPath = Path.GetFullPath(Path.Combine(root, entry.Project));
            var project = XDocument.Load(projectPath);
            foreach (var reference in project.Descendants().Where(node => node.Name.LocalName is "ProjectReference" or "PackageReference"))
                IsUi(reference.Attribute("Include")!.Value).ShouldBeFalse(projectPath);
        }
        var solution = File.ReadAllText(Path.Combine(root, "FluxFlow.sln"));
        solution.ShouldNotContain("Designer");
        solution.ShouldNotContain("FluxFlow.UI");
    }

    private static bool IsUi(string value)
        => new[] { "Designer", "FluxFlow.UI", "Blazor", "MudBlazor" }
            .Any(word => value.Contains(word, StringComparison.OrdinalIgnoreCase));
}
