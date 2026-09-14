using FluxFlow.Json.Model;
using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Json.Tests;

public sealed class ConfigurationValueTests
{
    [Fact]
    public async Task Build_passes_literal_json_values_without_placeholder_expansion_or_escaping()
    {
        var definition = ApplicationDefinitionJson.Deserialize("""
        {"Workflows":{"main":{"n":{"Type":"pass",
          "Text":"${Settings.Name}","Escaped":"$${Settings.Name}",
          "Object":{"Value":42},"Array":[null,true,"${Value}"]
        }}}}
        """);
        var original = FlowJson.Serialize(definition);
        ComponentDefinition? configured = null;
        var bindings = new ComponentBindings();
        bindings.Add("pass", value => { configured = value; return new Pass(); });
        await using var app = await FlowJson.BuildAsync(definition, bindings);
        configured.ShouldBeSameAs(definition.Workflows["main"].Components["n"]);
        configured!.Properties["Text"].GetString().ShouldBe("${Settings.Name}");
        configured.Properties["Escaped"].GetString().ShouldBe("$${Settings.Name}");
        configured.Properties["Object"].GetProperty("Value").GetInt32().ShouldBe(42);
        configured.Properties["Array"][0].ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);
        configured.Properties["Array"][1].GetBoolean().ShouldBeTrue();
        configured.Properties["Array"][2].GetString().ShouldBe("${Value}");
        FlowJson.Serialize(definition).ShouldBe(original);
    }

    private sealed class Pass : FlowTransformNode
    {
        protected override Task<FlowMessage> TransformAsync(FlowMessage message) => Task.FromResult(message);
    }
}
