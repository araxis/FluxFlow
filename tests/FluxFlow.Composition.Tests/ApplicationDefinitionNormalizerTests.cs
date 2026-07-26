using FluxFlow.Composition.Model;
using Shouldly;
using Xunit;

namespace FluxFlow.Composition.Tests;

public sealed class ApplicationDefinitionNormalizerTests
{
    [Fact]
    public void Normalizer_rewrites_component_and_resource_aliases_with_structured_diagnostics()
    {
        var registry = new ComponentCatalog(
        [
            new ComponentDescriptor(
                "data.map",
                static _ => throw new InvalidOperationException("Factory should not run."),
                aliases: ["flow.mapper"])
        ],
        [
            new ResourceTypeAliasDescriptor("resilience.retry", "retry.policy")
        ]);
        var normalizer = new ApplicationDefinitionNormalizer(registry);
        var definition = ApplicationDefinitionJson.Deserialize("""
            {
              "Resources": {
                "Policies": {
                  "Retry": { "Type": "resilience.retry", "Attempts": 3 }
                }
              },
              "Workflows": {
                "Orders": {
                  "Map": { "Type": "flow.mapper", "Expression": "payload" }
                }
              }
            }
            """);

        var result = normalizer.Normalize(definition);

        result.Changed.ShouldBeTrue();
        result.Diagnostics.Select(static diagnostic => diagnostic.Kind).ShouldBe([
            ApplicationDefinitionNormalizationDiagnosticKind.ResourceTypeAlias,
            ApplicationDefinitionNormalizationDiagnosticKind.ComponentTypeAlias
        ]);
        result.Diagnostics.Select(static diagnostic => diagnostic.Path).ShouldBe([
            "Resources.Policies.Retry.Type",
            "Workflows.Orders.Map.Type"
        ]);
        ((ResourceInstanceDefinition)((ResourceGroupDefinition)result.Definition.Resources["Policies"])
                .Resources["Retry"])
            .Type.ShouldBe("retry.policy");
        result.Definition.Workflows["Orders"].Components["Map"].Type.ShouldBe("data.map");
    }

    [Fact]
    public void Normalization_is_idempotent_and_preserves_an_already_canonical_instance()
    {
        var registry = new ComponentCatalog(
        [
            new ComponentDescriptor(
                "data.map",
                static _ => throw new InvalidOperationException("Factory should not run."),
                aliases: ["flow.mapper"])
        ]);
        var normalizer = new ApplicationDefinitionNormalizer(registry);
        var definition = ApplicationDefinitionJson.Deserialize("""
            {
              "Resources": {},
              "Workflows": {
                "Orders": {
                  "Map": { "Type": "flow.mapper" }
                }
              }
            }
            """);

        var first = normalizer.Normalize(definition);
        var second = normalizer.Normalize(first.Definition);

        first.Changed.ShouldBeTrue();
        second.Changed.ShouldBeFalse();
        second.Diagnostics.ShouldBeEmpty();
        second.Definition.ShouldBeSameAs(first.Definition);
    }

    [Fact]
    public void Normalizer_rejects_duplicate_resource_aliases()
    {
        Should.Throw<InvalidOperationException>(() =>
                new ComponentCatalog(
                    resourceTypeAliases:
                    [
                        new ResourceTypeAliasDescriptor("old", "first"),
                        new ResourceTypeAliasDescriptor("old", "second")
                    ]))
            .Message.ShouldContain("old");
    }
}
