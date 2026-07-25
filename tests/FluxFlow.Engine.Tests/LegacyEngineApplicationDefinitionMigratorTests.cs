using System.Text;
using System.Text.Json;
using FluxFlow.Engine.Migration;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.Tests;

public sealed class LegacyEngineApplicationDefinitionMigratorTests
{
    [Fact]
    public void Migrates_flat_ports_conditions_and_configuration()
    {
        const string json = """
            {
              "Resources": {},
              "Workflows": {
                "main": {
                  "Nodes": {
                    "source": {
                      "Type": "sample.source",
                      "Configuration": { "count": 3 }
                    },
                    "review": {
                      "Type": "sample.review",
                      "Input": "source.Output"
                    },
                    "priority": {
                      "Type": "sample.sink",
                      "When": "input.Priority == true",
                      "Configuration": { "category": "priority" },
                      "Input": [
                        "review.Output",
                        { "From": "other.Output", "When": "input.Override == true" }
                      ]
                    }
                  }
                }
              }
            }
            """;

        var definition = new LegacyEngineApplicationDefinitionMigrator().Migrate(json);

        var workflow = definition.Workflows["main"];
        workflow.Components.Keys.OrderBy(static value => value, StringComparer.Ordinal)
            .ShouldBe(["priority", "review", "source"]);
        workflow.Components["source"].Properties["count"].GetInt32().ShouldBe(3);
        workflow.Components["review"].Properties["Input"].GetString().ShouldBe("source.Output");

        var links = workflow.Components["priority"].Properties["Input"];
        links.ValueKind.ShouldBe(JsonValueKind.Array);
        var items = links.EnumerateArray().ToArray();
        items[0].GetProperty("Port").GetString().ShouldBe("review.Output");
        items[0].GetProperty("Condition").GetString().ShouldBe("input.Priority == true");
        items[1].GetProperty("Port").GetString().ShouldBe("other.Output");
        items[1].GetProperty("Condition").GetString().ShouldBe("input.Override == true");
    }

    [Fact]
    public void Utf8_migration_shortens_same_workflow_addresses()
    {
        var json = Encoding.UTF8.GetBytes("""
            {
              "Workflows": {
                "main": {
                  "Nodes": {
                    "source": { "Type": "sample.source" },
                    "sink": { "Type": "sample.sink", "Input": "main.source.Output" }
                  }
                }
              }
            }
            """);

        var definition = new LegacyEngineApplicationDefinitionMigrator().Migrate(json);

        definition.Workflows["main"].Components["sink"]
            .Properties["Input"].GetString().ShouldBe("source.Output");
    }

    [Fact]
    public void Rejects_executable_resource_nodes()
    {
        const string json = """
            {
              "Resources": {
                "clock": { "Type": "sample.clock" }
              },
              "Workflows": {}
            }
            """;

        Should.Throw<JsonException>(
                () => new LegacyEngineApplicationDefinitionMigrator().Migrate(json))
            .Message.ShouldContain("cannot be migrated automatically");
    }

    [Fact]
    public void Rejects_non_default_phase()
    {
        const string json = """
            {
              "Workflows": {
                "main": {
                  "Nodes": {
                    "source": { "Type": "sample.source", "Phase": 2 }
                  }
                }
              }
            }
            """;

        Should.Throw<JsonException>(
                () => new LegacyEngineApplicationDefinitionMigrator().Migrate(json))
            .Message.ShouldContain("processing profile");
    }

    [Fact]
    public void Rejects_flat_property_collision()
    {
        const string json = """
            {
              "Workflows": {
                "main": {
                  "Nodes": {
                    "sink": {
                      "Type": "sample.sink",
                      "Configuration": { "Input": "configured" },
                      "Input": "source.Output"
                    }
                  }
                }
              }
            }
            """;

        Should.Throw<JsonException>(
                () => new LegacyEngineApplicationDefinitionMigrator().Migrate(json))
            .Message.ShouldContain("both Configuration and its port declarations");
    }

    [Fact]
    public void Rejects_legacy_resource_links()
    {
        const string json = """
            {
              "Workflows": {
                "main": {
                  "Nodes": {
                    "sink": {
                      "Type": "sample.sink",
                      "Input": "Resources.clock.Output"
                    }
                  }
                }
              }
            }
            """;

        Should.Throw<JsonException>(
                () => new LegacyEngineApplicationDefinitionMigrator().Migrate(json))
            .Message.ShouldContain("legacy executable resource");
    }
}
