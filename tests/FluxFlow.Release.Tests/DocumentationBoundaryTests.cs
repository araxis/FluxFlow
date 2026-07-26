using Shouldly;
using Xunit;

namespace FluxFlow.Release.Tests;

public sealed class DocumentationBoundaryTests
{
    [Fact]
    public void Definition_docs_lead_with_canonical_composition_application_model()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var document = Path.Combine(root, "docs", "02-definitions-and-links.md");
        var text = File.ReadAllText(document);
        var defaultSection = text[..Math.Min(text.Length, 1_600)];

        defaultSection.Contains("FluxFlow.Composition.Model.ApplicationDefinition", StringComparison.Ordinal)
            .ShouldBeTrue("definition docs should lead with the canonical Composition application model.");
        defaultSection.Contains("`CompositionDefinition`", StringComparison.Ordinal)
            .ShouldBeFalse("definition docs must not lead with the retired runtime definition model.");

        var migrationSectionIndex = text.IndexOf("## Legacy Document Migration", StringComparison.Ordinal);
        migrationSectionIndex.ShouldBeGreaterThanOrEqualTo(
            0,
            "definition docs should document the explicit legacy conversion boundary.");

        var canonicalLoaderIndex = text.IndexOf("ApplicationDefinitionConfigurationLoader", StringComparison.Ordinal);
        canonicalLoaderIndex.ShouldBeInRange(
            0,
            migrationSectionIndex,
            "definition docs should show canonical loading before migration guidance.");
        text.Contains("LegacyCompositionDefinitionMigrator", StringComparison.Ordinal)
            .ShouldBeTrue("definition docs should identify the explicit migration API.");
        text.Contains("`CompositionDefinition`", StringComparison.Ordinal)
            .ShouldBeFalse("definition docs must not recommend the removed DTO.");
    }

    [Fact]
    public void Node_authoring_docs_keep_standalone_nodes_as_the_default_model()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var document = Path.Combine(root, "docs", "03-node-authoring.md");
        var text = File.ReadAllText(document);
        var defaultSection = text[..Math.Min(text.Length, 1_800)];

        defaultSection.Contains("FluxFlow.Nodes", StringComparison.Ordinal)
            .ShouldBeTrue("node authoring docs should lead with standalone node contracts.");
        defaultSection.Contains("FlowNode<TInput,TOutput>", StringComparison.Ordinal)
            .ShouldBeTrue("node authoring docs should show the standalone transform base type.");
        defaultSection.Contains("FlowSource<TOutput>", StringComparison.Ordinal)
            .ShouldBeTrue("node authoring docs should show the standalone source base type.");
        defaultSection.Contains("RuntimeNodeBuilder", StringComparison.Ordinal)
            .ShouldBeFalse("node authoring docs must not lead with engine runtime factories.");
        text.Contains("RuntimeNodeFactoryContext", StringComparison.Ordinal)
            .ShouldBeFalse("node authoring docs should not require engine factory context for normal nodes.");
    }

    [Fact]
    public void Package_authoring_docs_keep_engine_modules_out_of_default_package_shape()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var document = Path.Combine(root, "docs", "04-package-authoring.md");
        var text = File.ReadAllText(document);
        var defaultSection = text[..Math.Min(text.Length, 2_200)];

        defaultSection.Contains("standalone-node-first", StringComparison.Ordinal)
            .ShouldBeTrue("package authoring docs should lead with standalone packages.");
        defaultSection.Contains("FluxFlow.Nodes", StringComparison.Ordinal)
            .ShouldBeTrue("package authoring docs should keep node packages on FluxFlow.Nodes.");
        defaultSection.Contains("IServiceCollection", StringComparison.Ordinal)
            .ShouldBeTrue("package authoring docs should show DI-first optional component registration.");
        defaultSection.Contains("ComponentDescriptor", StringComparison.Ordinal)
            .ShouldBeTrue("package authoring docs should make immutable descriptors authoritative.");
        defaultSection.Contains("AddOrderComponents", StringComparison.Ordinal)
            .ShouldBeTrue("package authoring docs should show a family-level DI extension.");
        text.Contains("IFlowNodeModule", StringComparison.Ordinal)
            .ShouldBeFalse("package authoring docs should not require engine modules for normal component packages.");
        text.Contains("optional engine module", StringComparison.Ordinal)
            .ShouldBeFalse("package authoring docs should not list engine modules as a normal package layer.");
    }

    [Fact]
    public void Hosting_docs_keep_canonical_application_hosting_as_the_default_path()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var document = Path.Combine(root, "docs", "05-hosting-and-observability.md");
        var text = File.ReadAllText(document);
        var defaultSection = text[..Math.Min(text.Length, 1_000)];

        defaultSection.Contains("FluxFlow.Composition.Hosting", StringComparison.Ordinal)
            .ShouldBeTrue("hosting docs should lead with composition hosting.");
        defaultSection.Contains("IApplicationRevisionHost", StringComparison.Ordinal)
            .ShouldBeTrue("hosting docs should lead with the canonical application host API.");
        defaultSection.Contains("AddFluxFlowApplication", StringComparison.Ordinal)
            .ShouldBeTrue("hosting docs should lead with canonical application registration.");
        defaultSection.Contains("FlowApplicationHost", StringComparison.Ordinal)
            .ShouldBeFalse("hosting docs must not lead with the legacy engine host.");

        var migrationSectionIndex = text.IndexOf("## Legacy Application Conversion", StringComparison.Ordinal);
        migrationSectionIndex.ShouldBeGreaterThanOrEqualTo(
            0,
            "hosting docs should document conversion from retired application documents.");
        text.Contains("IApplicationRuntimeHost", StringComparison.Ordinal)
            .ShouldBeFalse("hosting docs must not recommend the removed composition host.");
        text.Contains("LegacyCompositionDefinitionMigrator", StringComparison.Ordinal)
            .ShouldBeTrue("hosting docs should identify the explicit migration API.");
        text.Contains("FlowApplicationHost", StringComparison.Ordinal)
            .ShouldBeFalse("canonical hosting docs should not direct users to the duplicate engine host model.");
    }

    [Fact]
    public void Workspace_docs_keep_canonical_application_projection_as_the_default_path()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var document = Path.Combine(root, "docs", "06-workspace-projection.md");
        var text = File.ReadAllText(document);
        var defaultSection = text[..Math.Min(text.Length, 1_400)];

        defaultSection.Contains("ApplicationDefinition", StringComparison.Ordinal)
            .ShouldBeTrue("workspace docs should lead with canonical application projection.");
        defaultSection.Contains("ToApplicationDefinition", StringComparison.Ordinal)
            .ShouldBeTrue("workspace docs should show the canonical projection boundary.");
        defaultSection.Contains("CompositionDefinition", StringComparison.Ordinal)
            .ShouldBeFalse("workspace docs must not lead with obsolete composition projection.");
        text.Contains("`CompositionDefinition`", StringComparison.Ordinal)
            .ShouldBeFalse("workspace docs must not project back into the removed model.");
    }

    [Fact]
    public void Validation_docs_keep_canonical_revision_validation_as_the_default_path()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var document = Path.Combine(root, "docs", "07-validation-and-errors.md");
        var text = File.ReadAllText(document);
        var defaultSection = text[..Math.Min(text.Length, 1_400)];

        defaultSection.Contains("ApplicationDefinitionNormalizer", StringComparison.Ordinal)
            .ShouldBeTrue("validation docs should include canonical alias normalization.");
        defaultSection.Contains("ApplicationLinkCompiler", StringComparison.Ordinal)
            .ShouldBeTrue("validation docs should include canonical link compilation.");
        defaultSection.Contains("IApplicationRevisionHost", StringComparison.Ordinal)
            .ShouldBeTrue("validation docs should include canonical revision activation.");
        defaultSection.Contains("CompositionValidator", StringComparison.Ordinal)
            .ShouldBeFalse("validation docs must not lead with obsolete composition validation.");
        defaultSection.Contains("ApplicationRuntimeBuilder", StringComparison.Ordinal)
            .ShouldBeFalse("validation docs must not lead with obsolete runtime construction.");
        text.Contains("There is no new universal `Errors` port.", StringComparison.Ordinal)
            .ShouldBeTrue("validation docs should preserve normal-result failure semantics.");
    }

    [Fact]
    public void Runtime_docs_keep_canonical_revision_lifecycle_as_the_default_path()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var document = Path.Combine(root, "docs", "08-runtime-states.md");
        var text = File.ReadAllText(document);
        var defaultSection = text[..Math.Min(text.Length, 1_600)];

        defaultSection.Contains("IApplicationRevisionHost", StringComparison.Ordinal)
            .ShouldBeTrue("runtime docs should lead with canonical revision lifecycle.");
        defaultSection.Contains("IApplicationRuntimeAccess", StringComparison.Ordinal)
            .ShouldBeTrue("runtime docs should lead with canonical stable port access.");
        defaultSection.Contains("CompositionRuntime", StringComparison.Ordinal)
            .ShouldBeFalse("runtime docs must not lead with the removed composition runtime name.");
        defaultSection.Contains("IApplicationRuntimeHost", StringComparison.Ordinal)
            .ShouldBeFalse("runtime docs must not lead with obsolete composition hosting.");

        var codeFirstIndex = text.IndexOf("## Code-First Runtime", StringComparison.Ordinal);
        codeFirstIndex.ShouldBeGreaterThanOrEqualTo(
            0,
            "runtime docs should explain the retained code-first lifecycle owner.");
        text.IndexOf("`ApplicationRuntime`", StringComparison.Ordinal).ShouldBeGreaterThan(
            codeFirstIndex,
            "ApplicationRuntime should only appear after the code-first heading.");
        text.Contains("IApplicationRuntimeHost", StringComparison.Ordinal)
            .ShouldBeFalse("runtime docs must not mention the removed composition host.");
    }

    [Fact]
    public void Json_docs_lead_with_canonical_composition_json()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var document = Path.Combine(root, "docs", "09-json-conversion.md");
        var text = File.ReadAllText(document);
        var defaultSection = text[..Math.Min(text.Length, 1_400)];

        defaultSection.Contains("FluxFlow.Composition.Model.ApplicationDefinition", StringComparison.Ordinal)
            .ShouldBeTrue("JSON docs should lead with the canonical Composition model.");
        defaultSection.Contains("ApplicationDefinitionJson", StringComparison.Ordinal)
            .ShouldBeTrue("JSON docs should show the canonical serializer before compatibility APIs.");
        defaultSection.Contains("CompositionDefinitionJson", StringComparison.Ordinal)
            .ShouldBeFalse("JSON docs must not lead with legacy runtime JSON APIs.");

        var migrationSectionIndex = text.IndexOf("## Legacy Document Migration", StringComparison.Ordinal);
        migrationSectionIndex.ShouldBeGreaterThanOrEqualTo(
            0,
            "JSON docs should keep legacy conversion in an explicit migration section.");
        text.Contains("CompositionDefinitionJson", StringComparison.Ordinal)
            .ShouldBeFalse("JSON docs must not recommend the removed serializer.");
        text.Contains("LegacyCompositionDefinitionMigrator", StringComparison.Ordinal)
            .ShouldBeTrue("JSON docs should identify the explicit migration API.");
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
        defaultSection.Contains("data.map", StringComparison.Ordinal)
            .ShouldBeTrue("expression docs should show composition mapper usage before optional engine APIs.");
        defaultSection.Contains("ApplicationRuntimeBuilder", StringComparison.Ordinal)
            .ShouldBeFalse("expression docs must not lead with optional engine build APIs.");
        defaultSection.Contains("FlowApplicationHost", StringComparison.Ordinal)
            .ShouldBeFalse("expression docs must not lead with optional engine host APIs.");

        var runtimeSectionIndex = text.IndexOf("## Canonical Runtime Link Conditions", StringComparison.Ordinal);
        runtimeSectionIndex.ShouldBeGreaterThanOrEqualTo(
            0,
            "expression docs should describe canonical runtime link conditions.");

        var assemblerIndex = text.IndexOf("ApplicationRuntimeAssembler", StringComparison.Ordinal);
        assemblerIndex.ShouldBeGreaterThan(
            runtimeSectionIndex,
            "canonical runtime link conditions should identify the assembler that activates them.");
        text.Contains("ApplicationRuntimeBuilder", StringComparison.Ordinal)
            .ShouldBeFalse("expression docs must not recommend the removed Engine runtime builder.");
        text.Contains("FlowApplicationHost", StringComparison.Ordinal)
            .ShouldBeFalse("expression docs must not recommend the removed Engine lifecycle host.");
    }

    [Fact]
    public void Component_composition_docs_keep_canonical_application_composition_as_the_default_path()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var document = Path.Combine(root, "docs", "12-component-composition.md");
        var text = File.ReadAllText(document);
        var defaultSection = text[..Math.Min(text.Length, 2_000)];

        defaultSection.Contains("ApplicationDefinition", StringComparison.Ordinal)
            .ShouldBeTrue("component composition docs should lead with canonical applications.");
        defaultSection.Contains("`CompositionDefinition`", StringComparison.Ordinal)
            .ShouldBeFalse("component composition docs must not lead with obsolete definitions.");
        defaultSection.Contains("Hosts own resources", StringComparison.Ordinal)
            .ShouldBeTrue("component composition docs should keep adapter resources host-owned.");
        text.Contains("Workflow.Component.Events", StringComparison.Ordinal)
            .ShouldBeTrue("component composition docs should document addressable component events.");
        text.Contains("LegacyCompositionDefinitionMigrator", StringComparison.Ordinal)
            .ShouldBeTrue("component composition docs should identify the migration boundary.");
        text.Contains("`CompositionDefinition`", StringComparison.Ordinal)
            .ShouldBeFalse("component composition docs must not recommend the removed DTO.");
        text.Contains("IFlowNodeModule", StringComparison.Ordinal)
            .ShouldBeFalse("component composition docs should not imply engine modules are required for component packages.");
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

    [Fact]
    public void Engine_docs_keep_canonical_assembler_as_the_only_runtime_model()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var readme = File.ReadAllText(
            Path.Combine(root, "src", "FluxFlow.Engine", "README.md"));
        var defaultSection = readme[..Math.Min(readme.Length, 2_400)];

        defaultSection.Contains("ApplicationRuntimeAssembler", StringComparison.Ordinal)
            .ShouldBeTrue("Engine docs should lead with canonical runtime assembly.");
        defaultSection.Contains("FluxFlow.Composition", StringComparison.Ordinal)
            .ShouldBeTrue("Engine docs should identify Composition as the application-model owner.");
        readme.Contains("LegacyEngineApplicationDefinitionMigrator", StringComparison.Ordinal)
            .ShouldBeTrue("Engine docs should expose the explicit legacy conversion boundary.");
        readme.Contains("FlowApplicationHost", StringComparison.Ordinal)
            .ShouldBeFalse("Engine docs must not recommend the removed lifecycle host.");
        readme.Contains("ApplicationRuntimeBuilder", StringComparison.Ordinal)
            .ShouldBeFalse("Engine docs must not recommend the removed runtime builder.");
        readme.Contains("FluxFlow.Engine.Definitions", StringComparison.Ordinal)
            .ShouldBeFalse("Engine docs must not retain the duplicate definition namespace.");

        var migration = File.ReadAllText(
            Path.Combine(root, "docs", "23-engine-2-to-3-migration.md"));
        migration.Contains("LegacyEngineApplicationDefinitionMigrator", StringComparison.Ordinal)
            .ShouldBeTrue("Engine migration docs should name the converter.");
        migration.Contains("executable resource nodes", StringComparison.OrdinalIgnoreCase)
            .ShouldBeTrue("Engine migration docs should identify the manual resource boundary.");
        migration.Contains("semantic processing profile", StringComparison.OrdinalIgnoreCase)
            .ShouldBeTrue("Engine migration docs should identify the phase replacement.");
    }
}
