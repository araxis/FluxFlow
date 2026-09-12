using System.Text.Json;
using FluxFlow.Mapping;
using Jsonata.Net.Native;
using Jsonata.Net.Native.Json;
using Shouldly;
using Xunit;

namespace FluxFlow.Expressions.Jsonata.Tests;

public sealed class JsonataFlowExpressionEngineTests
{
    [Fact]
    public async Task Native_configuration_uses_fresh_environments_and_invocation_local_bindings()
    {
        var created = 0;
        var engine = new JsonataFlowExpressionEngine(() =>
        {
            Interlocked.Increment(ref created);
            var environment = new EvaluationEnvironment { MaxDepth = 100 };
            environment.BindValue("offset", JToken.Parse("10"));
            environment.BindFunction("twice", (Func<int, int>)(value => value * 2));
            return environment;
        });
        var expression = engine.Compile<int>("$twice($input) + $offset + input");

        var values = await Task.WhenAll(Enumerable.Range(0, 32).Select(value => Task.Run(() =>
            expression.Evaluate(Context(JsonSerializer.SerializeToElement(value))))));

        values.ShouldBe(Enumerable.Range(0, 32).Select(static value => value * 3 + 10));
        created.ShouldBe(32);
        expression.Evaluate(new FlowMapContext
        {
            Variables = new Dictionary<string, object?> { ["input"] = 2, ["offset"] = 100 },
        }).ShouldBe(106);
        expression.Evaluate(Context(JsonSerializer.SerializeToElement(2))).ShouldBe(16);
        created.ShouldBe(34);
    }

    [Fact]
    public void Native_configuration_rejects_missing_factory_or_environment()
    {
        Should.Throw<ArgumentNullException>(() => new JsonataFlowExpressionEngine(null!));
        var engine = new JsonataFlowExpressionEngine(static () => null!);
        Should.Throw<InvalidOperationException>(() => engine.Compile<int>("input").Evaluate(Context(JsonSerializer.SerializeToElement(1))))
            .Message.ShouldContain("factory returned null");
    }

    private readonly JsonataFlowExpressionEngine _engine = new();
    private IFlowExpressionEngine Engine => _engine;

    [Fact]
    public void Name_identifies_jsonata()
        => _engine.Name.ShouldBe("jsonata");

    [Fact]
    public void Evaluate_maps_json_input_to_json_element()
    {
        var context = Context(JsonSerializer.SerializeToElement(new
        {
            firstName = "Ada",
            lastName = "Lovelace",
        }));

        var result = Engine.Evaluate<JsonElement>(
            "{'displayName': input.firstName & ' ' & input.lastName}",
            context);

        result.GetProperty("displayName").GetString().ShouldBe("Ada Lovelace");
    }

    [Theory]
    [InlineData("input.count", 3)]
    [InlineData("input.enabled", true)]
    public void Evaluate_converts_primitive_results(string expression, object expected)
    {
        var context = Context(JsonSerializer.SerializeToElement(new
        {
            count = 3,
            enabled = true,
        }));

        _engine.Evaluate(expression, context, expected.GetType()).ShouldBe(expected);
    }

    [Fact]
    public void Evaluate_returns_null_for_json_null()
    {
        var context = Context(JsonSerializer.SerializeToElement(new { value = (string?)null }));

        _engine.Evaluate("input.value", context, typeof(object)).ShouldBeNull();
    }

    [Fact]
    public void Evaluate_filters_non_serializable_runtime_variables()
    {
        var context = new FlowMapContext
        {
            Variables = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["input"] = JsonSerializer.SerializeToElement(new { value = "safe" }),
                ["runtimeType"] = typeof(string),
                ["callback"] = static () => { },
            },
        };

        var result = Engine.Evaluate<JsonElement>("input", context);

        result.GetProperty("value").GetString().ShouldBe("safe");
    }

    [Fact]
    public void Compile_reuses_a_valid_expression()
    {
        var compiled = _engine.Compile<JsonElement>("{'value': input.value}");

        compiled.Evaluate(Context(JsonSerializer.SerializeToElement(new { value = 1 })))
            .GetProperty("value").GetInt32().ShouldBe(1);
        compiled.Evaluate(Context(JsonSerializer.SerializeToElement(new { value = 2 })))
            .GetProperty("value").GetInt32().ShouldBe(2);
    }

    [Fact]
    public void Evaluate_rejects_blank_expression()
        => Should.Throw<ArgumentException>(() =>
            _engine.Evaluate(" ", Context(JsonSerializer.SerializeToElement(1)), typeof(object)));

    [Fact]
    public void Evaluate_surfaces_invalid_expression()
        => Should.Throw<Exception>(() =>
            _engine.Evaluate("input[", Context(JsonSerializer.SerializeToElement(1)), typeof(object)));

    private static FlowMapContext Context(JsonElement input) =>
        new()
        {
            Variables = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["input"] = input,
                ["value"] = input,
            },
        };
}
