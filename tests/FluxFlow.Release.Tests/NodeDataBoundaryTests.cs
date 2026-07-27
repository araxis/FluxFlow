using FluxFlow.Data;
using Shouldly;
using Xunit;

namespace FluxFlow.Release.Tests;

public sealed class NodeDataBoundaryTests
{
    [Fact]
    public void Nodes_owns_the_transport_neutral_data_namespace_without_a_compatibility_package()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        PackageManifest.Read(root).ShouldNotContain(entry =>
            string.Equals(entry.PackageId, "FluxFlow.Data", StringComparison.Ordinal));
        File.Exists(Path.Combine(root, "src", "FluxFlow.Data", "FluxFlow.Data.csproj"))
            .ShouldBeFalse("the retired package must not remain as a forwarding project.");

        var projectDirectory = Path.Combine(root, "src", "FluxFlow.Nodes");
        var dataDirectory = Path.Combine(projectDirectory, "Data");
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
        source.ShouldContain("namespace FluxFlow.Data;");

        typeof(FlowContent).Namespace.ShouldBe("FluxFlow.Data");
        typeof(FlowError).Namespace.ShouldBe("FluxFlow.Data");
        typeof(FlowContent).Assembly.GetName().Name.ShouldBe("FluxFlow.Nodes");
        typeof(FlowError).Assembly.ShouldBeSameAs(typeof(FlowContent).Assembly);
    }
}
