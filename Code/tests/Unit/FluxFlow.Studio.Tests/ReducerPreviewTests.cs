using System.Text.Json;
using FluxFlow.Authoring;
using FluxFlow.Authoring.Definitions.Reducer;
using FluxFlow.Authoring.Evaluation;
using FluxFlow.Expressions.Jsonata;
using FluxFlow.Expressions.CSharp;
using FluxFlow.Plugins;
using FluxFlow.Plugins.Transformation;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Studio.Tests;

public sealed class ReducerPreviewTests
{
    private static ServiceProvider Services()
    {
        var services = new ServiceCollection().AddJsonataExpressionEngine().AddCSharpExpressionEngine();
        new TransformationPlugin().ConfigureHost(services);
        return services.BuildServiceProvider();
    }

    private static ExpressionEvaluationRequest Request(bool equality, string expression, string input) => new()
    {
        Engine = "jsonata", PreviewProvider = equality ? ReducerDescription.EqualityPreviewKey : ReducerDescription.ReducePreviewKey,
        ResultKind = equality ? ExpressionResultKind.Boolean : ExpressionResultKind.Value,
        PreviewSettings = new() { ["eNgInE"] = JsonSerializer.SerializeToElement("jsonata") },
        Expression = expression, Input = input
    };

    [Theory]
    [InlineData(false, "state + input", "{\"state\":4,\"input\":3}", "7")]
    [InlineData(true, "previous == current", "{\"previous\":4,\"current\":4}", "true")]
    public async Task Configured_CSharp_engine_uses_the_same_typed_variables(bool equality, string expression, string input, string expected)
    {
        using var services = Services();
        var request = Request(equality, expression, input) with
        {
            Engine = "csharp", PreviewSettings = new() { ["Engine"] = JsonSerializer.SerializeToElement("csharp") }
        };
        var result = await new ExpressionEvaluator(services).EvaluateAsync(request);
        result.Error.ShouldBeNull(result.Details);
        result.Result!.Value.GetRawText().ShouldBe(expected);
    }

    [Theory]
    [InlineData(false, "$state + $input", "{\"state\":4,\"input\":3}", "7")]
    [InlineData(false, "[$state, $input, $value, $payload]", "{\"state\":4,\"input\":3}", "[4,3,3,3]")]
    [InlineData(false, "{'total':$state.total + $input.amount}", "{\"state\":{\"total\":4},\"input\":{\"amount\":3}}", "{\"total\":7}")]
    [InlineData(false, "$state", "{\"state\":null,\"input\":null}", "null")]
    [InlineData(false, "$input", "{\"state\":{},\"input\":[1,null,{\"n\":2}]}", "[1,null,{\"n\":2}]")]
    [InlineData(true, "$previous = $current", "{\"previous\":{\"n\":1},\"current\":{\"n\":1}}", "true")]
    [InlineData(true, "$previous = $current", "{\"previous\":0,\"current\":1}", "false")]
    [InlineData(true, "$previous = $current", "{\"previous\":null,\"current\":null}", "true")]
    [InlineData(true, "$exists($input) or $exists($state) or $exists($messageId)", "{\"previous\":0,\"current\":1,\"input\":99,\"state\":99}", "false")]
    public async Task Preview_uses_runtime_context_without_mutation_or_accumulating_state(bool equality, string expression, string input, string expected)
    {
        using var services = Services();
        var evaluator = new ExpressionEvaluator(services);
        var request = Request(equality, expression, input);
        var original = JsonSerializer.Serialize(request, ExpressionEvaluationJsonContext.Default.ExpressionEvaluationRequest);
        var transported = JsonSerializer.Deserialize(original, ExpressionEvaluationJsonContext.Default.ExpressionEvaluationRequest)!;
        for (var iteration = 0; iteration < 2; iteration++)
        {
            var result = await evaluator.EvaluateAsync(transported);
            result.Error.ShouldBeNull(result.Details);
            result.Result!.Value.GetRawText().ShouldBe(expected);
        }
        JsonSerializer.Serialize(request, ExpressionEvaluationJsonContext.Default.ExpressionEvaluationRequest).ShouldBe(original);
    }

    [Theory]
    [InlineData(false, "(", "{\"state\":0,\"input\":1}", "Compile")]
    [InlineData(false, "$unknown()", "{\"state\":0,\"input\":1}", "Evaluate")]
    [InlineData(false, "missing", "{\"state\":0,\"input\":1}", "Evaluate")]
    [InlineData(false, "$state", "{\"input\":1}", "Evaluate")]
    [InlineData(false, "$state", "[]", "Evaluate")]
    [InlineData(false, "$state", "{", "Input")]
    [InlineData(true, "$previous = $current", "{\"previous\":0}", "Evaluate")]
    [InlineData(true, "{'not':'boolean'}", "{\"previous\":0,\"current\":1}", "Evaluate")]
    [InlineData(true, "null", "{\"previous\":0,\"current\":1}", "Evaluate")]
    [InlineData(false, "file:unopened.jsonata", "{}", "Source")]
    [InlineData(true, "./unopened.jsonata", "{}", "Source")]
    public async Task Errors_report_the_stage_and_correction_recovers(bool equality, string expression, string input, string stage)
    {
        using var services = Services();
        var evaluator = new ExpressionEvaluator(services);
        var result = await evaluator.EvaluateAsync(Request(equality, expression, input));
        result.Stage.ShouldBe(stage); result.Error.ShouldNotBeNullOrWhiteSpace(); result.Details.ShouldNotBeNullOrWhiteSpace();
        result.Result.ShouldBeNull();
        var corrected = await evaluator.EvaluateAsync(Request(false, "base64:JHN0YXRlICsgJGlucHV0", "{\"state\":2,\"input\":3}"));
        corrected.Error.ShouldBeNull(corrected.Details); corrected.Result!.Value.GetInt32().ShouldBe(5);
    }

    [Fact]
    public async Task Engine_errors_missing_provider_and_cancellation_leave_configuration_unchanged()
    {
        using var services = Services();
        var evaluator = new ExpressionEvaluator(services);
        var request = Request(false, "$state + $input", "{\"state\":0,\"input\":1}");
        var unknown = await evaluator.EvaluateAsync(request with { Engine = "missing", PreviewSettings = new() { ["Engine"] = JsonSerializer.SerializeToElement("missing") } });
        unknown.Stage.ShouldBe("Compile"); unknown.Error.ShouldNotBeNullOrWhiteSpace();
        using var absent = new ServiceCollection().AddJsonataExpressionEngine().BuildServiceProvider();
        (await new ExpressionEvaluator(absent).EvaluateAsync(request)).Stage.ShouldBe("Engine");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(() => evaluator.EvaluateAsync(request, cancelled.Token));
        (await evaluator.EvaluateAsync(request)).Result!.Value.GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task Concurrent_preview_samples_do_not_share_or_advance_state()
    {
        using var services = Services();
        var evaluator = new ExpressionEvaluator(services);
        var requests = Enumerable.Range(0, 12).Select(index => Request(false, "$state.total + $input.amount",
            JsonSerializer.Serialize(new { state = new { total = index }, input = new { amount = 2 } }))).ToArray();
        var originals = requests.Select(request => JsonSerializer.Serialize(request)).ToArray();
        for (var pass = 0; pass < 2; pass++)
        {
            var results = await Task.WhenAll(requests.Select(request => evaluator.EvaluateAsync(request)));
            for (var index = 0; index < results.Length; index++)
            {
                results[index].Error.ShouldBeNull(results[index].Details);
                results[index].Result!.Value.GetInt32().ShouldBe(index + 2);
                JsonSerializer.Serialize(requests[index]).ShouldBe(originals[index]);
            }
        }
    }

    [Fact]
    public void Portable_metadata_roundtrip_preserves_custom_samples_and_old_catalog_defaults()
    {
        var json = JsonSerializer.Serialize(new[] { new TransformationPlugin().Description }, PluginJsonContext.Default.PluginDescriptionArray);
        var component = JsonSerializer.Deserialize(json, PluginJsonContext.Default.PluginDescriptionArray)!.Single().Components.Single(c => c.Palette.ComponentType == "reducer");
        var options = component.Inspector.Sections.SelectMany(s => s.Options).ToArray();
        var reduce = options.Single(o => o.Name == "Reduce"); var equal = options.Single(o => o.Name == "AreEqual");
        reduce.PreviewProvider.ShouldBe(ReducerDescription.ReducePreviewKey);
        equal.PreviewProvider.ShouldBe(ReducerDescription.EqualityPreviewKey);
        equal.ExpressionResult.ShouldBe(ExpressionResultKind.Boolean);
        reduce.PreviewFields.ShouldBe(new[] { "Engine" });
        using var reduceSample = JsonDocument.Parse(reduce.PreviewSample!);
        using var equalSample = JsonDocument.Parse(equal.PreviewSample!);
        reduceSample.RootElement.GetProperty("state").GetInt32().ShouldBe(0);
        equalSample.RootElement.GetProperty("current").GetInt32().ShouldBe(1);
        OptionEditorModel.Create("Expression", "Expression").PreviewSample.ShouldBeNull();
    }
}
