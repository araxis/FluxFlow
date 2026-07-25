using System.Text;
using System.Text.Json;
using FluxFlow.Composition.Migration;
using FluxFlow.Composition.Model;
using Microsoft.Extensions.Configuration;
using Shouldly;
using Xunit;

namespace FluxFlow.Composition.Tests;

public sealed class LegacyCompositionDefinitionMigratorTests
{
    [Fact]
    public void Migrates_wrapped_nodes_options_resources_and_links_to_flat_components()
    {
        var definition = new LegacyCompositionDefinitionMigrator().Migrate("""
            {
              "workflows": {
                "orders": {
                  "nodes": {
                    "source": {
                      "type": "sample.source",
                      "configuration": { "Count": 2 }
                    },
                    "sink": {
                      "type": "sample.sink",
                      "configuration": { "Label": "archive" },
                      "resources": { "Clock": "clock-key" }
                    }
                  },
                  "links": [
                    { "from": "source.Output", "to": "sink.Input" }
                  ]
                }
              }
            }
            """);

        definition.Resources.ShouldBeEmpty();
        var components = definition.Workflows["orders"].Components;
        components.Keys.ShouldBe(["sink", "source"], ignoreOrder: true);
        components["source"].Properties["Count"].GetInt32().ShouldBe(2);
        components["sink"].Properties["Label"].GetString().ShouldBe("archive");
        components["sink"].Properties["Clock"].GetString().ShouldBe("clock-key");
        components["sink"].Properties["Input"].GetString().ShouldBe("source.Output");

        var canonical = ApplicationDefinitionJson.Serialize(definition);
        canonical.ShouldBe(
            "{\"Resources\":{},\"Workflows\":{\"orders\":{" +
            "\"sink\":{\"Type\":\"sample.sink\",\"Clock\":\"clock-key\"," +
            "\"Input\":\"source.Output\",\"Label\":\"archive\"}," +
            "\"source\":{\"Type\":\"sample.source\",\"Count\":2}}}}");
        canonical.ShouldNotContain("Configuration");
        canonical.ShouldNotContain("Nodes");
        canonical.ShouldNotContain("Links");
    }

    [Fact]
    public void Migrates_cross_workflow_object_references_and_fan_in()
    {
        var definition = new LegacyCompositionDefinitionMigrator().Migrate("""
            {
              "Workflows": {
                "ingress": {
                  "Nodes": {
                    "first": { "Type": "sample.source" },
                    "second": { "Type": "sample.source" }
                  },
                  "Links": [
                    {
                      "From": { "Node": "first", "Port": "Output" },
                      "To": { "Workflow": "processing", "Node": "sink", "Port": "Input" }
                    },
                    {
                      "From": "second.Output",
                      "To": "processing.sink.Input"
                    }
                  ]
                },
                "processing": {
                  "Nodes": {
                    "sink": { "Type": "sample.sink" }
                  }
                }
              }
            }
            """);

        definition.Workflows["processing"].Components["sink"]
            .Properties["Input"]
            .EnumerateArray()
            .Select(static item => item.GetString())
            .ShouldBe(["ingress.first.Output", "ingress.second.Output"]);
    }

    [Fact]
    public void Configuration_migration_is_explicit_and_outputs_canonical_json()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("""
            {
              "FluxFlow": {
                "Composition": {
                  "workflows": {
                    "idle": {}
                  }
                }
              }
            }
            """));
        var configuration = new ConfigurationBuilder().AddJsonStream(stream).Build();

        var definition = new LegacyCompositionDefinitionMigrator().Migrate(configuration);

        ApplicationDefinitionJson.Serialize(definition).ShouldBe(
            "{\"Resources\":{},\"Workflows\":{\"idle\":{}}}");
    }

    [Fact]
    public void Rejects_ambiguous_flattening_and_existing_link_properties()
    {
        var migrator = new LegacyCompositionDefinitionMigrator();

        Should.Throw<JsonException>(() => migrator.Migrate("""
            {
              "workflows": {
                "main": {
                  "nodes": {
                    "node": {
                      "type": "sample",
                      "configuration": { "Client": "option" },
                      "resources": { "Client": "resource" }
                    }
                  }
                }
              }
            }
            """))
            .Message.ShouldContain("both Configuration and Resources");

        Should.Throw<JsonException>(() => migrator.Migrate("""
            {
              "workflows": {
                "main": {
                  "nodes": {
                    "source": { "type": "source" },
                    "sink": {
                      "type": "sink",
                      "configuration": { "Input": "configured" }
                    }
                  },
                  "links": [
                    { "from": "source.Output", "to": "sink.Input" }
                  ]
                }
              }
            }
            """))
            .Message.ShouldContain("conflicts with an existing component property");
    }

    [Theory]
    [InlineData("{\"Resources\":{},\"Workflows\":{}}")]
    [InlineData("{\"workflows\":{},\"other\":true}")]
    [InlineData("{\"workflows\":{\"main\":{\"nodes\":{\"node\":{}}}}}")]
    [InlineData("{\"workflows\":{\"main\":{\"nodes\":{},\"links\":[{\"from\":\"missing.Output\",\"to\":\"missing.Input\"}]}}}")]
    public void Rejects_non_legacy_or_lossy_shapes(string json)
        => Should.Throw<JsonException>(() =>
            new LegacyCompositionDefinitionMigrator().Migrate(json));
}
