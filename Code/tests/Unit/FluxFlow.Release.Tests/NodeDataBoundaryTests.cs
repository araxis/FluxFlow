using FluxFlow.Core.Messaging;
using Shouldly;
using Xunit;

namespace FluxFlow.Release.Tests;

public sealed class NodeDataBoundaryTests
{
    [Fact]
    public void Core_namespaces_follow_source_folders()
    {
        var directory = Path.Combine(ReleaseTestPaths.FindRepositoryRoot(), "src", "FluxFlow.Core");
        foreach (var file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(directory, file);
            if (relative.StartsWith("obj" + Path.DirectorySeparatorChar) ||
                relative.StartsWith("bin" + Path.DirectorySeparatorChar)) continue;
            var folder = Path.GetDirectoryName(relative)!;
            var expected = "FluxFlow.Core" + (folder.Length == 0 ? "" : "." + folder.Replace(Path.DirectorySeparatorChar, '.'));
            var source = File.ReadAllText(file);
            if (!System.Text.RegularExpressions.Regex.IsMatch(source, @"^namespace ",
                    System.Text.RegularExpressions.RegexOptions.Multiline)) continue;
            source.ShouldContain($"namespace {expected};", customMessage: relative);
        }
    }

    [Fact]
    public void Core_owns_the_transport_neutral_data_namespace_without_compatibility_packages()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        PackageManifest.Read(root).ShouldNotContain(entry =>
            string.Equals(entry.PackageId, "FluxFlow.Data", StringComparison.Ordinal));
        File.Exists(Path.Combine(root, "src", "FluxFlow.Data", "FluxFlow.Data.csproj"))
            .ShouldBeFalse("the retired package must not remain as a forwarding project.");

        PackageManifest.Read(root).ShouldNotContain(entry => entry.PackageId == "FluxFlow.Nodes" || entry.PackageId == "FluxFlow.Coordination");
        File.Exists(Path.Combine(root, "src", "Core", "Foundation", "FluxFlow.Nodes", "FluxFlow.Nodes.csproj")).ShouldBeFalse();
        File.Exists(Path.Combine(root, "src", "Core", "Execution", "FluxFlow.Coordination", "FluxFlow.Coordination.csproj")).ShouldBeFalse();
        var projectDirectory = Path.Combine(root, "src", "FluxFlow.Core");
        var dataDirectory = Path.Combine(projectDirectory, "Messaging");
        Directory.Exists(dataDirectory).ShouldBeTrue();
        var source = string.Join(
            '\n',
            Directory
                .EnumerateFiles(dataDirectory, "*.cs", SearchOption.TopDirectoryOnly)
                .Order(StringComparer.Ordinal)
                .Select(File.ReadAllText));

        source.ShouldNotContain("System.Threading.Tasks.Dataflow", Case.Insensitive);
        source.ShouldNotContain("FluxFlow.Composition", Case.Insensitive);
        source.ShouldNotContain("FluxFlow.Engine", Case.Insensitive);
        source.ShouldContain("namespace FluxFlow.Core.Messaging;");

        typeof(FlowValue).Namespace.ShouldBe("FluxFlow.Core.Messaging");
        typeof(FlowError).Namespace.ShouldBe("FluxFlow.Core.Messaging");
        typeof(FlowValue).Assembly.GetName().Name.ShouldBe("FluxFlow.Core");
        typeof(FlowError).Assembly.ShouldBeSameAs(typeof(FlowValue).Assembly);
    }
}
