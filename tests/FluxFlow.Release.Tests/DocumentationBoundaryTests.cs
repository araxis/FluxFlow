using Shouldly;
using Xunit;

namespace FluxFlow.Release.Tests;

public sealed class DocumentationBoundaryTests
{
    [Fact]
    public void Hosting_docs_keep_composition_hosting_as_the_default_path()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var document = Path.Combine(root, "docs", "05-hosting-and-observability.md");
        var text = File.ReadAllText(document);
        var defaultSection = text[..Math.Min(text.Length, 1_000)];

        defaultSection.Contains("FluxFlow.Composition.Hosting", StringComparison.Ordinal)
            .ShouldBeTrue("hosting docs should lead with composition hosting.");
        defaultSection.Contains("ICompositionRuntimeHost", StringComparison.Ordinal)
            .ShouldBeTrue("hosting docs should show the composition host API before optional engine APIs.");
        defaultSection.Contains("FlowApplicationHost", StringComparison.Ordinal)
            .ShouldBeFalse("hosting docs must not lead with the optional engine host.");

        var optionalEngineSectionIndex = text.IndexOf("## Optional Engine Host", StringComparison.Ordinal);
        optionalEngineSectionIndex.ShouldBeGreaterThanOrEqualTo(
            0,
            "hosting docs should keep engine hosting in an explicitly optional section.");

        var engineHostIndex = text.IndexOf("FlowApplicationHost", StringComparison.Ordinal);
        engineHostIndex.ShouldBeGreaterThan(
            optionalEngineSectionIndex,
            "FlowApplicationHost should only appear after the optional engine host heading.");
    }

    [Fact]
    public void Current_docs_keep_mapping_contracts_out_of_engine_namespace()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var documents = new[]
        {
            Path.Combine(root, "docs", "14-public-api-overview.md"),
            Path.Combine(root, "docs", "15-engine-compatibility.md")
        };

        foreach (var document in documents)
        {
            var text = File.ReadAllText(document);
            var fileName = Path.GetFileName(document);

            text.Contains("FluxFlow.Engine.Mapping", StringComparison.Ordinal)
                .ShouldBeFalse($"{fileName} must not document the removed engine mapping namespace.");
            text.Contains("FluxFlow.Mapping", StringComparison.Ordinal)
                .ShouldBeTrue($"{fileName} must document the standalone mapping package.");
        }
    }
}
