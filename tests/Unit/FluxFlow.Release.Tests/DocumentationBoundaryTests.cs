using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Release.Tests;

public sealed class DocumentationBoundaryTests
{
    [Fact]
    public void Hosting_and_lifetime_guides_describe_the_minimum_application()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var hosting = File.ReadAllText(Path.Combine(root, "docs", "05-hosting-and-observability.md"));
        hosting.ShouldContain("AddFluxFlow()");
        hosting.ShouldContain("FlowApplication");
        hosting.ShouldNotContain("AddFluxFlowUpdates");
        var lifetime = File.ReadAllText(Path.Combine(root, "docs", "08-runtime-states.md"));
        lifetime.ShouldContain("BuildApplicationAsync");
        lifetime.ShouldContain("FlowError");
    }

    [Fact]
    public void Registration_docs_use_native_factories_and_ports()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var normalDocuments = new[]
        {
            Path.Combine(root, "docs", "03-node-authoring.md"),
            Path.Combine(root, "docs", "04-package-authoring.md"),
            Path.Combine(root, "docs", "39-typed-code-first-definition.md"),
            Path.Combine(root, "docs", "40-component-registration.md"),
            Path.Combine(root, "src", "FluxFlow.Core", "README.md")
        };
        var normal = string.Join(
            Environment.NewLine,
            normalDocuments.Select(File.ReadAllText));

        normal.Contains("AddInputPort", StringComparison.Ordinal).ShouldBeTrue();
        normal.Contains("AddComponentFactory", StringComparison.Ordinal).ShouldBeTrue();
        normal.Contains("InputPortHandle", StringComparison.Ordinal).ShouldBeFalse();
        normal.Contains("OutputPortHandle", StringComparison.Ordinal).ShouldBeFalse();
        normal.Contains("AddComponent", StringComparison.Ordinal).ShouldBeTrue();
        normal.Contains("AddRuntimeComponent(", StringComparison.Ordinal).ShouldBeFalse();

        var publicOverview = File.ReadAllText(
            Path.Combine(root, "docs", "14-public-api-overview.md"));
        publicOverview.Contains("ResourceRegistration", StringComparison.Ordinal)
            .ShouldBeTrue();
        publicOverview.Contains("ApplicationRuntime`", StringComparison.Ordinal)
            .ShouldBeFalse();
    }

    [Fact]
    public void Configuration_docs_use_sections_directly()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var document = Path.Combine(root, "docs", "02-definitions-and-links.md");
        var text = File.ReadAllText(document);
        var defaultSection = text[..Math.Min(text.Length, 1_600)];

        defaultSection.Contains("IConfigurationSection", StringComparison.Ordinal)
            .ShouldBeTrue("definition docs should lead with the canonical Composition application model.");
        defaultSection.Contains("`CompositionDefinition`", StringComparison.Ordinal)
            .ShouldBeFalse("definition docs must not lead with the retired runtime definition model.");

        text.Contains("LegacyCompositionDefinitionMigrator", StringComparison.Ordinal)
            .ShouldBeFalse("definition docs must describe only the canonical configuration path.");
        text.Contains("`CompositionDefinition`", StringComparison.Ordinal)
            .ShouldBeFalse("definition docs must not recommend the removed DTO.");
    }

    [Fact]
    public void Node_definition_docs_keep_standalone_nodes_as_the_default_model()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var document = Path.Combine(root, "docs", "03-node-authoring.md");
        var text = File.ReadAllText(document);
        var defaultSection = text[..Math.Min(text.Length, 1_800)];

        defaultSection.Contains("FluxFlow.Core.Nodes", StringComparison.Ordinal)
            .ShouldBeTrue("node definition docs should lead with standalone node contracts.");
        defaultSection.Contains("`FlowNode`", StringComparison.Ordinal)
            .ShouldBeTrue("node definition docs should show the standalone transform base type.");
        defaultSection.Contains("`FlowSource`", StringComparison.Ordinal)
            .ShouldBeTrue("node definition docs should show the standalone source base type.");
        defaultSection.Contains("RuntimeNodeBuilder", StringComparison.Ordinal)
            .ShouldBeFalse("node definition docs must not lead with engine runtime factories.");
        text.Contains("RuntimeNodeFactoryContext", StringComparison.Ordinal)
            .ShouldBeFalse("node definition docs should not require engine factory context for normal nodes.");
    }

    [Fact]
    public void Package_definition_docs_keep_engine_modules_out_of_default_package_shape()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var document = Path.Combine(root, "docs", "04-package-authoring.md");
        var text = File.ReadAllText(document);
        var defaultSection = text[..Math.Min(text.Length, 2_200)];

        defaultSection.Contains("standalone-node-first", StringComparison.Ordinal)
            .ShouldBeTrue("package definition docs should lead with standalone packages.");
        defaultSection.Contains("FluxFlow.Core.Nodes", StringComparison.Ordinal)
            .ShouldBeTrue("package definition docs should keep node packages on FluxFlow.Nodes.");
        defaultSection.Contains("IServiceCollection", StringComparison.Ordinal)
            .ShouldBeTrue("package definition docs should show direct IServiceCollection component registration.");
        (defaultSection.Contains("services.AddComponentFactory(", StringComparison.Ordinal) &&
         defaultSection.Contains("AddInputPort<", StringComparison.Ordinal) &&
         defaultSection.Contains("AddOutputPort<", StringComparison.Ordinal) &&
         defaultSection.Contains("AddEventPort<", StringComparison.Ordinal))
            .ShouldBeTrue("package authoring should register native factories and ports outside component logic.");
        defaultSection.Contains("AddOrders", StringComparison.Ordinal)
            .ShouldBeTrue("package definition docs should show a family-level registration extension.");
        text.Contains("IFlowNodeModule", StringComparison.Ordinal)
            .ShouldBeFalse("package definition docs should not require engine modules for normal component packages.");
        text.Contains("optional engine module", StringComparison.Ordinal)
            .ShouldBeFalse("package definition docs should not list engine modules as a normal package layer.");
    }

    [Fact]
    public void Historical_workspace_docs_identify_the_old_projection_boundary()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var document = Path.Combine(root, "docs", "06-workspace-projection.md");
        var text = File.ReadAllText(document);
        text.ShouldContain("Historical API material");
        var defaultSection = text;

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
    public void Json_docs_lead_with_canonical_composition_json()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var document = Path.Combine(root, "docs", "09-json-conversion.md");
        var text = File.ReadAllText(document);
        var defaultSection = text[..Math.Min(text.Length, 1_400)];

        defaultSection.Contains("IConfigurationSection", StringComparison.Ordinal)
            .ShouldBeTrue("JSON docs should use native configuration sections.");
        defaultSection.Contains("AddFluxFlowApplicationAsync", StringComparison.Ordinal)
            .ShouldBeTrue("JSON docs should show direct configuration registration.");
        defaultSection.Contains("CompositionDefinitionJson", StringComparison.Ordinal)
            .ShouldBeFalse("JSON docs must not lead with legacy runtime JSON APIs.");

        text.Contains("CompositionDefinitionJson", StringComparison.Ordinal)
            .ShouldBeFalse("JSON docs must not recommend the removed serializer.");
        text.Contains("LegacyCompositionDefinitionMigrator", StringComparison.Ordinal)
            .ShouldBeFalse("JSON docs must describe only canonical deserialization.");
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

        var applicationIndex = text.IndexOf("FlowApplication", runtimeSectionIndex, StringComparison.Ordinal);
        applicationIndex.ShouldBeGreaterThan(
            runtimeSectionIndex,
            "canonical runtime link conditions should identify the application that activates them.");
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
            .ShouldBeFalse("component composition docs must not expose legacy conversion.");
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
            text.Contains("FluxFlow.Core.Mapping", StringComparison.Ordinal)
                .ShouldBeTrue($"{fileName} must document the current mapping namespace.");
        }
    }

    [Fact]
    public void Configuration_loading_docs_use_direct_reader()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        string[] documents =
        [
            Path.Combine(root, "docs", "02-definitions-and-links.md"),
            Path.Combine(root, "docs", "09-json-conversion.md")
        ];

        foreach (var document in documents)
        {
            var text = File.ReadAllText(document);
            var fileName = Path.GetFileName(document);

            text.Contains("AddFluxFlowApplicationAsync", StringComparison.Ordinal)
                .ShouldBeTrue(
                    $"{fileName} must demonstrate the direct configuration reader.");
            text.Contains("ApplicationDefinitionConfigurationLoader", StringComparison.Ordinal)
                .ShouldBeFalse(
                    $"{fileName} must not demonstrate the removed configuration loader.");
        }
    }

    [Fact]
    public void Historical_migration_inventory_preserves_its_original_definition_source_boundary()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var migration = File.ReadAllText(
            Path.Combine(root, "docs", "23-engine-2-to-3-migration.md"));
        const string inventoryHeading = "## Removed Public Surface Inventory";
        const string inventoryEndMarker = "Removed members from retained types include";
        var inventoryIndex = migration.IndexOf(inventoryHeading, StringComparison.Ordinal);
        inventoryIndex.ShouldBeGreaterThanOrEqualTo(0);
        var inventoryEndIndex = migration.IndexOf(
            inventoryEndMarker,
            inventoryIndex,
            StringComparison.Ordinal);

        inventoryEndIndex.ShouldBeGreaterThan(inventoryIndex);
        var removedTypeInventory = migration[inventoryIndex..inventoryEndIndex];
        removedTypeInventory.Contains(
                "ApplicationDefinitionConfigurationLoader",
                StringComparison.Ordinal)
            .ShouldBeTrue("The removed loader must remain in the breaking-change inventory.");
        string[] retainedTypes =
        [
            "IApplicationDefinitionSource",
            "ConfigurationApplicationDefinitionSource",
            "StaticApplicationDefinitionSource"
        ];
        foreach (var retainedType in retainedTypes)
        {
            removedTypeInventory.Contains(retainedType, StringComparison.Ordinal)
                .ShouldBeFalse(
                    $"The retained {retainedType} must not be classified as removed.");
        }
    }

    [Fact]
    public void Migration_inventory_remains_historical()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var migration = File.ReadAllText(
            Path.Combine(root, "docs", "23-engine-2-to-3-migration.md"));
        const string inventoryHeading = "## Removed Public Surface Inventory";
        var inventoryIndex = migration.IndexOf(inventoryHeading, StringComparison.Ordinal);
        inventoryIndex.ShouldBeGreaterThan(0);
        migration[..inventoryIndex]
            .Contains("LegacyEngineApplicationDefinitionMigrator", StringComparison.Ordinal)
            .ShouldBeFalse("Migration instructions must not recommend the removed converter.");
        migration[inventoryIndex..]
            .Contains("LegacyEngineApplicationDefinitionMigrator", StringComparison.Ordinal)
            .ShouldBeTrue("The breaking-change inventory must name the removed converter.");
        migration.Contains("executable resource nodes", StringComparison.OrdinalIgnoreCase)
            .ShouldBeTrue("Engine migration docs should identify the manual resource boundary.");
        migration.Contains("semantic processing profile", StringComparison.OrdinalIgnoreCase)
            .ShouldBeTrue("Engine migration docs should identify the phase replacement.");
    }
}
