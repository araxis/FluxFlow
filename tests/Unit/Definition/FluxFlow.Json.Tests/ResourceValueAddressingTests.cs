using System.Text.Json;
using FluxFlow.Core.Addressing;
using FluxFlow.Json.Model;
using Shouldly;
using Xunit;

namespace FluxFlow.Json.Tests;

public sealed class ResourceValueAddressingTests
{
    private const string Json = """
    {
      "resources": {
        "connections": {"Messaging": {"Primary": {"Type":"mqtt.broker","Host":"localhost","Ports":[1883,8883]}}},
        "Settings": {"Messaging":{"Topic":"orders","Kind":"literal-data","Optional":null},"BatchSize":100}
      },
      "Workflows": {
        "orders": {"Resources":{"Settings":{"BatchSize":25,"Local":{"Enabled":true}}}},
        "other": {}
      }
    }
    """;

    [Fact]
    public void Full_relative_and_object_context_paths_read_values_without_activation()
    {
        var definition = ApplicationDefinitionJson.Deserialize(Json);
        definition.GetResourceValue("resources.connections.Messaging.Primary.Host").GetString().ShouldBe("localhost");
        definition.GetResourceValue("Connections.Messaging.Primary").GetProperty("Host").GetString().ShouldBe("localhost");
        definition.GetResourceValue("Host", "Resources.Connections.Messaging.Primary").GetString().ShouldBe("localhost");
        definition.GetResourceValue("Connections.Messaging.Primary.Ports.1").GetInt32().ShouldBe(8883);
        definition.GetResourceValue("Settings.BatchSize", "orders").GetInt32().ShouldBe(25);
        definition.GetResourceValue("Resources.Settings.BatchSize", "orders").GetInt32().ShouldBe(100);
        definition.GetResourceValue("Settings.Messaging.Topic", "orders").GetString().ShouldBe("orders");
        definition.GetResourceValue("orders.Resources.Settings.Local.Enabled", "other").GetBoolean().ShouldBeTrue();
        definition.GetResourceValue("Settings.Messaging.Optional").ValueKind.ShouldBe(JsonValueKind.Null);
        Should.Throw<KeyNotFoundException>(() => definition.GetResourceValue("Settings.Local.Enabled", "other"));
        Should.Throw<KeyNotFoundException>(() => definition.GetResourceValue("Connections.Messaging.Primary.host"));
        Should.Throw<KeyNotFoundException>(() => definition.GetResourceValue("Connections.Messaging.Primary.Ports.9"));
    }

    [Fact]
    public void Objects_are_data_and_round_trip_without_group_or_value_discriminators()
    {
        var definition = ApplicationDefinitionJson.Deserialize(Json);
        var encoded = ApplicationDefinitionJson.Serialize(definition);
        var copy = ApplicationDefinitionJson.Deserialize(encoded);
        copy.GetResourceValue("Settings.Messaging").EnumerateObject().Select(p => p.Name)
            .ShouldBe(["Topic", "Kind", "Optional"]);
        copy.Resources.Keys.ShouldBe(["Connections", "Settings"]);
        ResourceInstanceDefinition.Enumerate(copy.Resources).Select(p => p.Key).ShouldBe(["Connections.Messaging.Primary"]);
        ApplicationDefinitionJson.Serialize(copy).ShouldBe(encoded);
    }

    [Fact]
    public void Json_values_remain_owned_after_source_document_is_disposed()
    {
        ResourceDefinition resource;
        using (var document = JsonDocument.Parse("""{"x":[true,null,{"y":2}]}"""))
            resource = new ResourceDefinition(document.RootElement);
        ApplicationAddress.TryReadJsonValue(resource.Value, ["x", "2", "y"], out var value).ShouldBeTrue();
        value.GetInt32().ShouldBe(2);
    }
}
