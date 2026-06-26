using Shouldly;
using Xunit;

namespace FluxFlow.Release.Tests;

public sealed class DocumentationBoundaryTests
{
    [Fact]
    public void Definition_docs_keep_composition_definition_as_the_default_model()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var document = Path.Combine(root, "docs", "02-definitions-and-links.md");
        var text = File.ReadAllText(document);
        var defaultSection = text[..Math.Min(text.Length, 1_200)];

        defaultSection.Contains("CompositionDefinition", StringComparison.Ordinal)
            .ShouldBeTrue("definition docs should lead with the composition definition model.");
        defaultSection.Contains("ApplicationDefinition", StringComparison.Ordinal)
            .ShouldBeFalse("definition docs must not lead with the optional engine definition model.");

        var optionalEngineSectionIndex = text.IndexOf("## Optional Engine Definition", StringComparison.Ordinal);
        optionalEngineSectionIndex.ShouldBeGreaterThanOrEqualTo(
            0,
            "definition docs should keep engine definitions in an explicitly optional section.");

        var fluentBuilderIndex = text.IndexOf("CompositionDefinitionBuilder", StringComparison.Ordinal);
        fluentBuilderIndex.ShouldBeInRange(
            0,
            optionalEngineSectionIndex,
            "definition docs should show the fluent composition builder before optional engine APIs.");

        var engineDefinitionIndex = text.IndexOf("ApplicationDefinition", StringComparison.Ordinal);
        engineDefinitionIndex.ShouldBeGreaterThan(
            optionalEngineSectionIndex,
            "ApplicationDefinition should only appear after the optional engine definition heading.");
    }

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
    public void Workspace_docs_keep_composition_projection_as_the_default_path()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var document = Path.Combine(root, "docs", "06-workspace-projection.md");
        var text = File.ReadAllText(document);
        var defaultSection = text[..Math.Min(text.Length, 1_400)];

        defaultSection.Contains("CompositionDefinition", StringComparison.Ordinal)
            .ShouldBeTrue("workspace docs should lead with composition projection.");
        defaultSection.Contains("ToCompositionDefinition", StringComparison.Ordinal)
            .ShouldBeTrue("workspace docs should show composition projection before optional engine APIs.");
        defaultSection.Contains("ApplicationDefinition", StringComparison.Ordinal)
            .ShouldBeFalse("workspace docs must not lead with optional engine projection.");

        var optionalEngineSectionIndex = text.IndexOf("## Optional Engine Projection", StringComparison.Ordinal);
        optionalEngineSectionIndex.ShouldBeGreaterThanOrEqualTo(
            0,
            "workspace docs should keep engine projection in an explicitly optional section.");

        var engineDefinitionIndex = text.IndexOf("ApplicationDefinition", StringComparison.Ordinal);
        engineDefinitionIndex.ShouldBeGreaterThan(
            optionalEngineSectionIndex,
            "ApplicationDefinition should only appear after the optional engine projection heading.");
    }

    [Fact]
    public void Validation_docs_keep_composition_validation_as_the_default_path()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var document = Path.Combine(root, "docs", "07-validation-and-errors.md");
        var text = File.ReadAllText(document);
        var defaultSection = text[..Math.Min(text.Length, 1_400)];

        defaultSection.Contains("CompositionValidator", StringComparison.Ordinal)
            .ShouldBeTrue("validation docs should lead with composition validation.");
        defaultSection.Contains("CompositionRuntimeBuilder", StringComparison.Ordinal)
            .ShouldBeTrue("validation docs should show the composition build API before optional engine APIs.");
        defaultSection.Contains("ApplicationRuntimeBuilder", StringComparison.Ordinal)
            .ShouldBeFalse("validation docs must not lead with the optional engine build API.");
        defaultSection.Contains("FlowApplicationHost", StringComparison.Ordinal)
            .ShouldBeFalse("validation docs must not lead with the optional engine host.");

        var optionalEngineSectionIndex = text.IndexOf("## Optional Engine Errors", StringComparison.Ordinal);
        optionalEngineSectionIndex.ShouldBeGreaterThanOrEqualTo(
            0,
            "validation docs should keep engine errors in an explicitly optional section.");

        var engineBuilderIndex = text.IndexOf("ApplicationRuntimeBuilder", StringComparison.Ordinal);
        engineBuilderIndex.ShouldBeGreaterThan(
            optionalEngineSectionIndex,
            "ApplicationRuntimeBuilder should only appear after the optional engine errors heading.");
    }

    [Fact]
    public void Runtime_docs_keep_composition_lifecycle_as_the_default_path()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var document = Path.Combine(root, "docs", "08-runtime-states.md");
        var text = File.ReadAllText(document);
        var defaultSection = text[..Math.Min(text.Length, 1_600)];

        defaultSection.Contains("CompositionRuntime", StringComparison.Ordinal)
            .ShouldBeTrue("runtime docs should lead with composition runtime lifecycle.");
        defaultSection.Contains("ICompositionRuntimeHost", StringComparison.Ordinal)
            .ShouldBeTrue("runtime docs should show composition hosting before optional engine state APIs.");
        defaultSection.Contains("FlowApplicationHostState", StringComparison.Ordinal)
            .ShouldBeFalse("runtime docs must not lead with optional engine host states.");
        defaultSection.Contains("ApplicationState", StringComparison.Ordinal)
            .ShouldBeFalse("runtime docs must not lead with optional engine runtime states.");

        var optionalEngineSectionIndex = text.IndexOf("## Optional Engine States", StringComparison.Ordinal);
        optionalEngineSectionIndex.ShouldBeGreaterThanOrEqualTo(
            0,
            "runtime docs should keep engine states in an explicitly optional section.");

        var engineStateIndex = text.IndexOf("FlowApplicationHostState", StringComparison.Ordinal);
        engineStateIndex.ShouldBeGreaterThan(
            optionalEngineSectionIndex,
            "FlowApplicationHostState should only appear after the optional engine states heading.");
    }

    [Fact]
    public void Json_docs_keep_composition_json_as_the_default_path()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var document = Path.Combine(root, "docs", "09-json-conversion.md");
        var text = File.ReadAllText(document);
        var defaultSection = text[..Math.Min(text.Length, 1_400)];

        defaultSection.Contains("CompositionConfigurationLoader", StringComparison.Ordinal)
            .ShouldBeTrue("JSON docs should lead with composition configuration loading.");
        defaultSection.Contains("CompositionDefinitionJson", StringComparison.Ordinal)
            .ShouldBeTrue("JSON docs should show composition serializer options before optional engine APIs.");
        defaultSection.Contains("ApplicationDefinitionJson", StringComparison.Ordinal)
            .ShouldBeFalse("JSON docs must not lead with optional engine JSON APIs.");

        var optionalEngineSectionIndex = text.IndexOf("## Optional Engine JSON", StringComparison.Ordinal);
        optionalEngineSectionIndex.ShouldBeGreaterThanOrEqualTo(
            0,
            "JSON docs should keep engine JSON in an explicitly optional section.");

        var engineJsonIndex = text.IndexOf("ApplicationDefinitionJson", StringComparison.Ordinal);
        engineJsonIndex.ShouldBeGreaterThan(
            optionalEngineSectionIndex,
            "ApplicationDefinitionJson should only appear after the optional engine JSON heading.");
    }

    [Fact]
    public void Expression_docs_keep_composition_mapping_as_the_default_path()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var document = Path.Combine(root, "docs", "10-expression-mapping.md");
        var text = File.ReadAllText(document);
        var defaultSection = text[..Math.Min(text.Length, 2_400)];

        defaultSection.Contains("FluxFlow.Mapping", StringComparison.Ordinal)
            .ShouldBeTrue("expression docs should lead with standalone mapping contracts.");
        defaultSection.Contains("flow.mapper", StringComparison.Ordinal)
            .ShouldBeTrue("expression docs should show composition mapper usage before optional engine APIs.");
        defaultSection.Contains("ApplicationRuntimeBuilder", StringComparison.Ordinal)
            .ShouldBeFalse("expression docs must not lead with optional engine build APIs.");
        defaultSection.Contains("FlowApplicationHost", StringComparison.Ordinal)
            .ShouldBeFalse("expression docs must not lead with optional engine host APIs.");

        var optionalEngineSectionIndex = text.IndexOf("## Optional Engine Link Conditions", StringComparison.Ordinal);
        optionalEngineSectionIndex.ShouldBeGreaterThanOrEqualTo(
            0,
            "expression docs should keep engine link conditions in an explicitly optional section.");

        var engineBuilderIndex = text.IndexOf("ApplicationRuntimeBuilder", StringComparison.Ordinal);
        engineBuilderIndex.ShouldBeGreaterThan(
            optionalEngineSectionIndex,
            "ApplicationRuntimeBuilder should only appear after the optional engine link condition heading.");
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
