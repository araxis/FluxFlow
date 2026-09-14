using System.Text.Json;
using FluxFlow.Core;
using FluxFlow.Data;
using FluxFlow.Json.Model;
using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Json.Tests;

public sealed class CallFunctionTests
{
    [Fact]
    public async Task Ordinary_component_calls_an_address_and_routes_reply_to_its_own_output()
    {
        const string json = """
        {"Workflows":{"main":{
          "call":{"Type":"CallFunction","RequestTo":"target.Input","ReplyFrom":"target.Output","Timeout":"00:00:05","Output":"after.Input"},
          "target":{"Type":"pass"},"after":{"Type":"pass"}
        }}}
        """;
        var bindings = new ComponentBindings();
        bindings.Add("pass", _ => new Pass());
        await using var app = await FlowJson.BuildAsync(json, bindings);
        await app.StartAsync();
        var result = await app.RequestAsync("main.call.Input", "main.after.Output",
            FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(42, new JsonSerializerOptions(JsonSerializerDefaults.Web)))), TimeSpan.FromSeconds(5));
        result.Value!.Json.Deserialize<int>(new JsonSerializerOptions(JsonSerializerDefaults.Web)).ShouldBe(42);
        app.PendingRequestCount.ShouldBe(0);
    }

    [Theory]
    [InlineData("{\"Resources\":{\"Functions\":{}},\"Workflows\":{}}")]
    [InlineData("{\"Workflows\":{\"w\":{\"Resources\":{\"functions\":{}}}}}")]
    public void Retired_function_sections_give_a_migration_error(string json)
        => Should.Throw<JsonException>(() => ApplicationDefinitionJson.Deserialize(json)).Message.ShouldContain("CallFunction");

    private sealed class Pass : FlowTransformNode
    {
        protected override Task<FlowMessage> TransformAsync(FlowMessage message) => Task.FromResult(message);
    }
}
