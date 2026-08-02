using FluxFlow.Composition.Model;
using Shouldly;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Xunit;
using CanonicalWorkflowDefinition = FluxFlow.Composition.Model.WorkflowDefinition;

namespace FluxFlow.Composition.Tests;

public sealed class ApplicationDefinitionJsonTests
{
    [Fact]
    public void CreateSerializerOptions_returns_fresh_mutable_instances()
    {
        var first = ApplicationDefinitionJson.CreateSerializerOptions(writeIndented: true);
        var second = ApplicationDefinitionJson.CreateSerializerOptions(writeIndented: true);

        first.ShouldNotBeSameAs(second);
        first.IsReadOnly.ShouldBeFalse();
        second.IsReadOnly.ShouldBeFalse();
        first.PropertyNameCaseInsensitive = true;
        first.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        first.WriteIndented = false;
        second.PropertyNameCaseInsensitive.ShouldBeFalse();
        second.PropertyNamingPolicy.ShouldBeNull();
        second.WriteIndented.ShouldBeTrue();
    }

    [Fact]
    public void Default_serialization_reuses_private_format_options_with_exact_output()
    {
        var definition = new ApplicationDefinition();
        const string compact = "{\"Resources\":{},\"Workflows\":{}}";
        var indented = "{\n  \"Resources\": {},\n  \"Workflows\": {}\n}"
            .Replace("\n", Environment.NewLine, StringComparison.Ordinal);

        ApplicationDefinitionJson.Serialize(definition).ShouldBe(compact);
        ApplicationDefinitionJson.Serialize(definition).ShouldBe(compact);
        ApplicationDefinitionJson.Serialize(definition, writeIndented: true).ShouldBe(indented);
        ApplicationDefinitionJson.Serialize(definition, writeIndented: true).ShouldBe(indented);
        ApplicationDefinitionJson.SerializeToUtf8Bytes(definition)
            .ShouldBe(Encoding.UTF8.GetBytes(compact));
        ApplicationDefinitionJson.SerializeToUtf8Bytes(definition, writeIndented: true)
            .ShouldBe(Encoding.UTF8.GetBytes(indented));

        var cachedOptions = typeof(ApplicationDefinitionJson)
            .GetFields(BindingFlags.Static | BindingFlags.NonPublic)
            .Where(field => field.FieldType == typeof(JsonSerializerOptions))
            .ToArray();
        cachedOptions.Length.ShouldBe(2);
        cachedOptions.ShouldAllBe(field => field.IsInitOnly);
        cachedOptions.Select(field => field.GetValue(null).ShouldBeOfType<JsonSerializerOptions>())
            .OrderBy(options => options.WriteIndented)
            .Select(options => (options.WriteIndented, options.IsReadOnly))
            .ShouldBe([(false, true), (true, true)]);
    }

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

    [Theory]
    [InlineData(
        "{\"Composition\":{\"Workflows\":{}}}",
        "supports only 'Resources' and 'Workflows'; found 'Composition'")]
    [InlineData(
        "{\"workflows\":{}}",
        "supports only 'Resources' and 'Workflows'; found 'workflows'")]
    [InlineData(
        "{\"Resources\":{},\"Workflows\":{\"main\":{\"Nodes\":{}}}}",
        "Component 'main.Nodes' requires a string 'Type' property")]
    [InlineData(
        "{\"Resources\":{},\"Workflows\":{\"main\":{\"Links\":[]}}}",
        "Component 'main.Links' must be a JSON object")]
    public void Legacy_document_shapes_fail_with_canonical_contract_diagnostics(
        string json,
        string expectedMessage)
    {
        var exception = Should.Throw<JsonException>(
            () => ApplicationDefinitionJson.Deserialize(json));

        exception.Message.ShouldContain(expectedMessage);
    }

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
    public void Omitted_component_defaults_remain_omitted_after_round_trip()
    {
        const string json =
            "{\"Resources\":{},\"Workflows\":{\"Main\":{\"Worker\":{\"Type\":\"sample.worker\"}}}}";

        var definition = ApplicationDefinitionJson.Deserialize(json);
        var component = definition.Workflows["Main"].Components["Worker"];

        component.Properties.ShouldBeEmpty();
        ApplicationDefinitionJson.Serialize(definition).ShouldBe(json);
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
        Should.Throw<ArgumentException>(() => new ComponentDefinition(
            "sample",
            [new("configuration", document.RootElement)]));
    }
}
