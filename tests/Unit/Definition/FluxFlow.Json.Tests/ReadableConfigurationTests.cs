using FluxFlow.Data;
using System.Text.Json;
using FluxFlow.Core;
using FluxFlow.Core.Addressing;
using FluxFlow.Json.Model;
using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Json.Tests;

public sealed class ReadableConfigurationTests
{
    [Fact]
    public void Round_trip_preserves_order_at_every_level_and_normalizes_only_structural_names()
    {
        const string json = """
        {"workflows":{"Z":{"z":{"Topic":"Mixed/CASE","type":"Example.Node","Settings":{"z":1,"A":2},"output":["a.Input",{"condition":"KeepCase(input)","port":"a.Input"}]},"expressionengine":"Rules","a":{"Type":"Example.Node"}},"A":{}},"resources":{"z":{"Host":"MixedCase","type":"Example.Resource"},"a":{"second":{"Type":"X"},"first":{"Type":"Y"}}},"expressionengine":"Default"}
        """;
        var definition = ApplicationDefinitionJson.Deserialize(json);
        var serialized = ApplicationDefinitionJson.Serialize(definition);
        using var document = JsonDocument.Parse(serialized);
        var root = document.RootElement;
        Names(root).ShouldBe(["Workflows", "Resources", "ExpressionEngine"]);
        Names(root.GetProperty("Workflows")).ShouldBe(["Z", "A"]);
        var workflow = root.GetProperty("Workflows").GetProperty("Z");
        Names(workflow).ShouldBe(["z", "ExpressionEngine", "a"]);
        var component = workflow.GetProperty("z");
        Names(component).ShouldBe(["Topic", "Type", "Settings", "Output"]);
        component.GetProperty("Topic").GetString().ShouldBe("Mixed/CASE");
        component.GetProperty("Type").GetString().ShouldBe("Example.Node");
        Names(component.GetProperty("Settings")).ShouldBe(["z", "A"]);
        Names(component.GetProperty("Output")[1]).ShouldBe(["condition", "port"]);
        Names(root.GetProperty("Resources")).ShouldBe(["z", "a"]);
        Names(root.GetProperty("Resources").GetProperty("z")).ShouldBe(["Host", "Type"]);
        Names(root.GetProperty("Resources").GetProperty("a")).ShouldBe(["second", "first"]);
        ApplicationDefinitionJson.Serialize(ApplicationDefinitionJson.Deserialize(serialized)).ShouldBe(serialized);
    }

    [Fact]
    public async Task Missing_resources_and_lowercase_port_properties_build_the_same_core_application()
    {
        var bindings = new ComponentBindings();
        bindings.Add("pass", _ => new Pass());
        await using var app = await FlowJson.BuildAsync("""
        {"workflows":{"main":{"source":{"type":"pass","output":{"port":"sink.Input","delivery":"reliable"}},"sink":{"TYPE":"pass"}}}}
        """, bindings);
        await app.StartAsync();
        var reply = await app.RequestAsync("main.source.Input", "main.sink.Output",
            FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement("ok", new JsonSerializerOptions(JsonSerializerDefaults.Web)))), TimeSpan.FromSeconds(5));
        reply.Value!.Json.Deserialize<string>(new JsonSerializerOptions(JsonSerializerDefaults.Web)).ShouldBe("ok");
        var definition = ApplicationDefinitionJson.Deserialize("""{"workflows":{}}""");
        ApplicationDefinitionJson.Serialize(definition).ShouldNotContain("Resources");
    }

    [Theory]
    [InlineData("client", "Resources.client")]
    [InlineData("group.client", "Resources.group.client")]
    [InlineData("resources.group.client", "Resources.group.client")]
    [InlineData("RESOURCES.group.Client", "Resources.group.Client")]
    [InlineData("one.two.client", "Resources.one.two.client")]
    public void Resource_paths_have_one_exact_root_relative_meaning(string value, string expected)
        => ApplicationAddress.ResolveResource(value).Value.ShouldBe(expected);

    [Theory]
    [InlineData("resources")]
    [InlineData("group..client")]
    [InlineData(" group.client")]
    public void Invalid_resource_paths_are_rejected(string value)
        => ApplicationAddress.TryResolveResource(value, out _).ShouldBeFalse();

    [Theory]
    [InlineData("""{"Resources":{},"resources":{},"Workflows":{}}""")]
    [InlineData("""{"Workflows":{"main":{"n":{"Type":"x","type":"y"}}}}""")]
    [InlineData("""{"Workflows":{"main":{"n":{"Type":"x","Output":"n.Input","output":"n.Input"}}}}""")]
    public void Duplicate_structural_aliases_are_rejected(string json)
        => Should.Throw<JsonException>(() => ApplicationDefinitionJson.Deserialize(json));

    private static string[] Names(JsonElement element) => element.EnumerateObject().Select(p => p.Name).ToArray();
    private sealed class Pass : FlowTransformNode
    {
        protected override Task<FlowMessage> TransformAsync(FlowMessage message)
            => Task.FromResult(message);
    }
}
