using System.Text.Json;
using System.Text.Json.Serialization;
using FluxFlow.Mapping;
using Jsonata.Net.Native;
using Jsonata.Net.Native.Json;
using Shouldly;
using Xunit;

namespace FluxFlow.Expressions.Jsonata.Tests;

public sealed class InvocationBindingTests
{
    [Fact]
    public void Root_and_named_binding_use_one_serialization_per_invocation()
    {
        var value = new SnapshotProbe();
        var context = new FlowMapContext
        {
            Variables = new Dictionary<string, object?> { ["input"] = value }
        };
        var expression = new JsonataFlowExpressionEngine()
            .Compile<JsonElement>("{'root': input.value, 'bound': $input.value}");

        var first = expression.Evaluate(context);
        value.ReadCount.ShouldBe(1);
        first.GetProperty("root").GetInt32().ShouldBe(1);
        first.GetProperty("bound").GetInt32().ShouldBe(1);

        var second = expression.Evaluate(context);
        value.ReadCount.ShouldBe(2);
        second.GetProperty("root").GetInt32().ShouldBe(2);
        second.GetProperty("bound").GetInt32().ShouldBe(2);
        context.Variables["input"].ShouldBeSameAs(value);
    }

    [Fact]
    public void Native_binding_mutation_cannot_change_root_siblings_or_later_invocations()
    {
        var input = JsonSerializer.SerializeToElement(new { count = 1 });
        var context = new FlowMapContext
        {
            Variables = new Dictionary<string, object?> { ["input"] = input, ["other"] = input }
        };
        var created = 0;
        var engine = new JsonataFlowExpressionEngine(() =>
        {
            created++;
            var environment = new EvaluationEnvironment();
            environment.BindFunction("mutate", (Func<JToken, int>)(value =>
            {
                ((JObject)value).Set("count", JToken.Parse("99"));
                return 0;
            }));
            return environment;
        });
        var expression = engine.Compile<JsonElement>(
            "($mutate($input); {'root': input.count, 'bound': $input.count, 'other': $other.count})");

        foreach (var result in new[] { expression.Evaluate(context), expression.Evaluate(context) })
        {
            result.GetProperty("root").GetInt32().ShouldBe(1);
            result.GetProperty("bound").GetInt32().ShouldBe(99);
            result.GetProperty("other").GetInt32().ShouldBe(1);
        }
        created.ShouldBe(2);
        input.GetProperty("count").GetInt32().ShouldBe(1);
        ((JsonElement)context.Variables["input"]!).GetProperty("count").GetInt32().ShouldBe(1);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("true")]
    [InlineData("17")]
    [InlineData("\"text\"")]
    [InlineData("[1,null,false]")]
    [InlineData("{\"nested\":{\"values\":[1,2.5]},\"empty\":[],\"absent\":null}")]
    public void Root_and_native_binding_preserve_the_same_json_value(string json)
    {
        using var document = JsonDocument.Parse(json);
        var input = document.RootElement.Clone();
        var context = new FlowMapContext
        {
            Variables = new Dictionary<string, object?> { ["input"] = input }
        };
        var result = new JsonataFlowExpressionEngine().Compile<JsonElement>(
            "{'root': input, 'bound': $input}").Evaluate(context);

        JsonElement.DeepEquals(result.GetProperty("root"), input).ShouldBeTrue();
        JsonElement.DeepEquals(result.GetProperty("bound"), input).ShouldBeTrue();
    }

    private sealed class SnapshotProbe
    {
        private int _reads;
        public int Value => ++_reads;
        [JsonIgnore] public int ReadCount => _reads;
    }
}
