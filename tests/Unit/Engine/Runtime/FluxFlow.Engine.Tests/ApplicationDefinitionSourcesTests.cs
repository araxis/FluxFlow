using System.Text;
using System.Text.Json;
using FluxFlow.Composition.Model;
using Microsoft.Extensions.Configuration;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.Tests;

public sealed class ApplicationDefinitionSourcesTests
{
    [Fact]
    public async Task Configuration_source_preserves_boolean_resource_and_component_properties_through_definition_round_trip()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Application:Resources:Flags:Type"] = "test.flags",
                ["Application:Resources:Flags:ResourceEnabled"] = "True",
                ["Application:Resources:Flags:ResourceDisabled"] = "False",
                ["Application:Workflows:Main:Switch:Type"] = "switch",
                ["Application:Workflows:Main:Switch:ComponentEnabled"] = "True",
                ["Application:Workflows:Main:Switch:ComponentDisabled"] = "False"
            })
            .Build();

        var definition = await new ConfigurationApplicationDefinitionSource(
                configuration,
                "Application")
            .LoadAsync();

        AssertBooleanProperties(definition);

        var json = ApplicationDefinitionJson.Serialize(definition);
        json.ShouldContain("\"ResourceEnabled\":true");
        json.ShouldContain("\"ResourceDisabled\":false");
        json.ShouldContain("\"ComponentEnabled\":true");
        json.ShouldContain("\"ComponentDisabled\":false");
        json.ShouldNotContain("\"ResourceEnabled\":\"True\"");
        json.ShouldNotContain("\"ResourceDisabled\":\"False\"");
        json.ShouldNotContain("\"ComponentEnabled\":\"True\"");
        json.ShouldNotContain("\"ComponentDisabled\":\"False\"");

        AssertBooleanProperties(ApplicationDefinitionJson.Deserialize(json));
    }

    [Fact]
    public async Task Configuration_source_reconstructs_the_canonical_tree_in_engine()
    {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(
            """
            {
              "Application": {
                "Resources": {},
                "Workflows": {
                  "Main": {
                    "Source": {
                      "Type": "source",
                      "Values": [1, 2]
                    }
                  }
                }
              }
            }
            """));
        var configuration = new ConfigurationBuilder()
            .AddJsonStream(stream)
            .Build();

        var definition = await new ConfigurationApplicationDefinitionSource(
                configuration,
                "Application")
            .LoadAsync();

        definition.Resources.ShouldBeEmpty();
        var source = definition.Workflows["Main"].Components["Source"];
        source.Type.ShouldBe("source");
        source.Properties["Values"].GetArrayLength().ShouldBe(2);
    }

    private static void AssertBooleanProperties(ApplicationDefinition definition)
    {
        var resource = definition.Resources["Flags"]
            .ShouldBeOfType<ResourceInstanceDefinition>();
        resource.Type.ShouldBe("test.flags");
        AssertBoolean(resource.Properties, "ResourceEnabled", expected: true);
        AssertBoolean(resource.Properties, "ResourceDisabled", expected: false);

        var component = definition.Workflows["Main"].Components["Switch"];
        component.Type.ShouldBe("switch");
        AssertBoolean(component.Properties, "ComponentEnabled", expected: true);
        AssertBoolean(component.Properties, "ComponentDisabled", expected: false);
    }

    private static void AssertBoolean(
        IReadOnlyDictionary<string, JsonElement> properties,
        string name,
        bool expected)
    {
        var value = properties[name];
        value.ValueKind.ShouldBe(expected ? JsonValueKind.True : JsonValueKind.False);
        value.GetBoolean().ShouldBe(expected);
    }
}
