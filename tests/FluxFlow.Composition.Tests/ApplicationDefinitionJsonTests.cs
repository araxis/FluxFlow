using FluxFlow.Composition.Model;
using Shouldly;
using System.Text.Json;
using Xunit;
using CanonicalWorkflowDefinition = FluxFlow.Composition.Model.WorkflowDefinition;

namespace FluxFlow.Composition.Tests;

public sealed class ApplicationDefinitionJsonTests
{
    [Fact]
    public void CanonicalJsonReadsFlatDefinitionsAndWritesDeterministically()
    {
        const string json = """
        {
          "Resources": {
            "Messaging": {
              "Server": {
                "Host": "localhost",
                "Type": "sample.server"
              },
              "Client": {
                "Broker": "Resources.Messaging.Server",
                "Type": "sample.client"
              }
            }
          },
          "Workflows": {
            "Orders": {
              "Source": {
                "Count": 2,
                "Type": "sample.source"
              },
              "Sink": {
                "Input": [
                  "Source.Output",
                  { "Port": "Other.Source.Output", "Condition": "value != null" }
                ],
                "Type": "sample.sink"
              }
            }
          }
        }
        """;

        var definition = ApplicationDefinitionJson.Deserialize(json);

        var messaging = definition.Resources["Messaging"]
            .ShouldBeOfType<ResourceGroupDefinition>();
        var client = messaging.Resources["Client"]
            .ShouldBeOfType<ResourceInstanceDefinition>();
        client.Type.ShouldBe("sample.client");
        client.Properties["Broker"].GetString().ShouldBe("Resources.Messaging.Server");
        definition.Workflows["Orders"].Components["Sink"]
            .Properties["Input"].GetArrayLength().ShouldBe(2);

        const string expected =
            "{\"Resources\":{\"Messaging\":{" +
            "\"Client\":{\"Type\":\"sample.client\",\"Broker\":\"Resources.Messaging.Server\"}," +
            "\"Server\":{\"Type\":\"sample.server\",\"Host\":\"localhost\"}}}," +
            "\"Workflows\":{\"Orders\":{" +
            "\"Sink\":{\"Type\":\"sample.sink\",\"Input\":[\"Source.Output\"," +
            "{\"Condition\":\"value != null\",\"Port\":\"Other.Source.Output\"}]}," +
            "\"Source\":{\"Type\":\"sample.source\",\"Count\":2}}}}";

        ApplicationDefinitionJson.Serialize(definition).ShouldBe(expected);
        JsonSerializer.Serialize(definition).ShouldBe(expected);
        ApplicationDefinitionJson.Serialize(
            ApplicationDefinitionJson.Deserialize(expected)).ShouldBe(expected);
    }

    [Fact]
    public void ModelCopiesCollectionsWithOrdinalNamesAndOwnedJsonValues()
    {
        using var document = JsonDocument.Parse("{\"enabled\":true}");
        var properties = new Dictionary<string, JsonElement>
        {
            ["Options"] = document.RootElement
        };
        var components = new Dictionary<string, ComponentDefinition>
        {
            ["Reader"] = new("sample.reader", properties)
        };
        KeyValuePair<string, CanonicalWorkflowDefinition>[] workflows =
        [
            new("Orders", new(components)),
            new("orders", new())
        ];

        var definition = new ApplicationDefinition(workflows: workflows);
        properties.Clear();
        components.Clear();
        workflows[0] = new("Changed", new());

        definition.Workflows.Count.ShouldBe(2);
        definition.Workflows.ContainsKey("Orders").ShouldBeTrue();
        definition.Workflows.ContainsKey("orders").ShouldBeTrue();
        definition.Workflows["Orders"].Components["Reader"]
            .Properties["Options"].GetProperty("enabled").GetBoolean().ShouldBeTrue();
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"Resources\":{}}")]
    [InlineData("{\"Resources\":{},\"Workflows\":{},\"Other\":{}}")]
    [InlineData("{\"resources\":{},\"Workflows\":{}}")]
    [InlineData("{\"Resources\":[],\"Workflows\":{}}")]
    [InlineData("{\"Resources\":{},\"Workflows\":[]}")]
    [InlineData("{\"Resources\":{},\"Workflows\":{\"Orders\":[]}}")]
    [InlineData("{\"Resources\":{},\"Workflows\":{\"Orders\":{\"Node\":{}}}}")]
    [InlineData("{\"Resources\":{},\"Workflows\":{\"Orders\":{\"Node\":{\"Type\":1}}}}")]
    [InlineData("{\"Resources\":{\"Group\":{\"Leaf\":\"value\"}},\"Workflows\":{}}")]
    [InlineData("{\"Resources\":{},\"Workflows\":{\"Orders\":{\"Node\":{\"Type\":\"sample\",\"Configuration\":{}}}}}")]
    [InlineData("{\"Resources\":{},\"Workflows\":{\"Orders\":{\"Node\":{\"Type\":\"sample\",\"Resources\":{}}}}}")]
    [InlineData("{\"Resources\":{\"Type\":{}},\"Workflows\":{}}")]
    [InlineData("{\"Resources\":{},\"Workflows\":{\"Orders\":{\"Node.Main\":{\"Type\":\"sample\"}}}}")]
    [InlineData("{\"Resources\":{},\"Workflows\":{\"Orders\":{\"Node\":{\"type\":\"sample\"}}}}")]
    [InlineData("{\"Resources\":{},\"Workflows\":{\"Orders.Main\":{}}}")]
    [InlineData("{\"Resources\":{},\"Workflows\":{\"System\":{}}}")]
    [InlineData("{\"Resources\":{},\"Workflows\":{\" Orders\":{}}}")]
    [InlineData("{\"Resources\":{},\"Resources\":{},\"Workflows\":{}}")]
    [InlineData("{\"Resources\":{},\"Workflows\":{\"Orders\":{\"Node\":{\"Type\":\"sample\",\"Options\":{\"a\":1,\"a\":2}}}}}")]
    public void CanonicalJsonRejectsNonCanonicalShapes(string json)
        => Should.Throw<JsonException>(() => ApplicationDefinitionJson.Deserialize(json));

    [Fact]
    public void ResourceLeavesRequireTypeWhileGroupsDoNotEmitType()
    {
        var definition = new ApplicationDefinition(
            resources:
            [
                new("Group", new ResourceGroupDefinition(
                [
                    new("Leaf", new ResourceInstanceDefinition("sample.resource"))
                ]))
            ]);

        ApplicationDefinitionJson.Serialize(definition).ShouldBe(
            "{\"Resources\":{\"Group\":{\"Leaf\":{\"Type\":\"sample.resource\"}}}," +
            "\"Workflows\":{}}");
    }

    [Fact]
    public void PublicConstructorsRejectDuplicateAndReservedNames()
    {
        Should.Throw<ArgumentException>(() => new ApplicationDefinition(
            workflows:
            [
                new("Orders", new CanonicalWorkflowDefinition()),
                new("Orders", new CanonicalWorkflowDefinition())
            ]));
        Should.Throw<ArgumentException>(() => new CanonicalWorkflowDefinition(
            [new("Node.Main", new ComponentDefinition("sample"))]));
        Should.Throw<ArgumentException>(() => new ResourceGroupDefinition(
            [new("Type", new ResourceGroupDefinition())]));

        using var document = JsonDocument.Parse("1");
        Should.Throw<ArgumentException>(() => new ComponentDefinition(
            "sample",
            [new("Configuration", document.RootElement)]));
    }
}
