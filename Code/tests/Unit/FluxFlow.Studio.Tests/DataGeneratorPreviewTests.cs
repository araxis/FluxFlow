using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluxFlow.Authoring;
using FluxFlow.Authoring.Evaluation;
using FluxFlow.Expressions.Jsonata;
using FluxFlow.Plugins;
using FluxFlow.Plugins.Sources;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Studio.Tests;

public sealed class DataGeneratorPreviewTests
{
    private const string Provider = "FluxFlow.Plugins.Sources.dataGenerator";

    private static ServiceProvider Services()
    {
        var services = new ServiceCollection().AddJsonataExpressionEngine();
        new SourcesPlugin().ConfigureHost(services);
        return services.BuildServiceProvider();
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("sv-SE")]
    [InlineData("fa-IR")]
    public async Task Concurrent_previews_keep_their_locale_and_numeric_format_independent_of_host_culture(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            using var services = Services();
            var evaluator = new ExpressionEvaluator(services);
            await Task.WhenAll(new string?[] { "sv", "en", null, "sv" }.Select(async locale =>
            {
                var request = new ExpressionEvaluationRequest
                {
                    Engine = "jsonata", PreviewProvider = Provider,
                    PreviewSettings = locale is null ? [] : new() { ["Locale"] = JsonSerializer.SerializeToElement(locale) },
                    Expression = "{'day':$fakeWeekday(),'number':$fakeDecimal(1.5,1.5)}"
                };
                var result = await evaluator.EvaluateAsync(request);
                result.Error.ShouldBeNull(result.Details);
                var value = result.Result!.Value;
                var expected = CultureInfo.GetCultureInfo(locale == "sv" ? "sv-SE" : "en-US").DateTimeFormat.DayNames;
                expected.ShouldContain(day => string.Equals(day, value.GetProperty("day").GetString(), StringComparison.OrdinalIgnoreCase));
                value.GetProperty("number").GetDecimal().ShouldBe(1.5m);
            }));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("en")]
    [InlineData("sv")]
    public async Task Preview_uses_generator_functions_sample_metadata_and_selected_locale_without_mutation(string? locale)
    {
        using var services = Services();
        var request = new ExpressionEvaluationRequest
        {
            Engine = "jsonata", PreviewProvider = Provider,
            PreviewSettings = locale is null ? [] : new() { ["lOcAlE"] = JsonSerializer.SerializeToElement(locale) },
            Expression = "{'id': id, 'number': $fakeInt(7,7), 'name': $fakeFirstName(), 'month': $fakeMonth(), 'message': $messageId, 'site': $headers.site}",
            Input = "{\"id\":42}", Headers = "{\"site\":\"test\"}"
        };
        var original = JsonSerializer.Serialize(request, ExpressionEvaluationJsonContext.Default.ExpressionEvaluationRequest);
        // Exercise the same source-generated transport contract used by browser preview.
        var transported = JsonSerializer.Deserialize(original, ExpressionEvaluationJsonContext.Default.ExpressionEvaluationRequest)!;
        var result = await new ExpressionEvaluator(services).EvaluateAsync(transported);
        result.Error.ShouldBeNull(result.Details);
        var value = result.Result!.Value;
        value.GetProperty("id").GetInt32().ShouldBe(42);
        value.GetProperty("number").GetInt32().ShouldBe(7);
        value.GetProperty("name").GetString().ShouldNotBeNullOrWhiteSpace();
        value.GetProperty("month").GetString().ShouldNotBeNullOrWhiteSpace();
        value.GetProperty("site").GetString().ShouldBe("test");
        value.GetProperty("message").GetString().ShouldBe(JsonNode.Parse(request.Context)!["MessageId"]!.GetValue<string>());
        JsonSerializer.Serialize(request, ExpressionEvaluationJsonContext.Default.ExpressionEvaluationRequest).ShouldBe(original);
    }

    [Theory]
    [InlineData("(", "en", "Compile")]
    [InlineData("$fakeInt(1,1)", "not-a-locale", "Compile")]
    [InlineData("$fakeInt(1,1)", "", "Compile")]
    [InlineData("$fakeInt(9,1)", "en", "Evaluate")]
    [InlineData("$unknownFunction()", "en", "Evaluate")]
    [InlineData("missing", "en", "Evaluate")]
    [InlineData("file:unopened.jsonata", "en", "Source")]
    [InlineData("./unopened.jsonata", "en", "Source")]
    public async Task Validation_reports_the_original_stage_and_recovers(string expression, string locale, string stage)
    {
        using var services = Services();
        var evaluator = new ExpressionEvaluator(services);
        var request = new ExpressionEvaluationRequest { Engine = "jsonata", PreviewProvider = Provider, Expression = expression,
            PreviewSettings = new() { ["Locale"] = JsonSerializer.SerializeToElement(locale) } };
        var invalid = await evaluator.EvaluateAsync(request);
        invalid.Stage.ShouldBe(stage); invalid.Error.ShouldNotBeNullOrWhiteSpace(); invalid.Details.ShouldNotBeNullOrWhiteSpace();
        invalid.Result.ShouldBeNull();
        var valid = await evaluator.EvaluateAsync(request with { Expression = "base64:JGZha2VJbnQoMiwgMik=", PreviewSettings = [] });
        valid.Error.ShouldBeNull(valid.Details); valid.Result!.Value.GetInt32().ShouldBe(2);
    }

    [Fact]
    public async Task Provider_is_optional_isolated_and_does_not_change_plain_mapper_evaluation()
    {
        using var services = Services();
        var evaluator = new ExpressionEvaluator(services);
        var plain = new ExpressionEvaluationRequest { Engine = "jsonata", Expression = "$fakeInt(1,1)" };
        (await evaluator.EvaluateAsync(plain)).Error.ShouldNotBeNull();
        (await evaluator.EvaluateAsync(plain with { PreviewProvider = Provider })).Error.ShouldBeNull();
        (await evaluator.EvaluateAsync(plain)).Error.ShouldNotBeNull();
        using var missing = new ServiceCollection().AddJsonataExpressionEngine().BuildServiceProvider();
        var absent = await new ExpressionEvaluator(missing).EvaluateAsync(plain with { PreviewProvider = Provider });
        absent.Stage.ShouldBe("Engine"); absent.Error!.ShouldContain("not available");
        (await new ExpressionEvaluator(missing).EvaluateAsync(plain with { Expression = "42" })).Result!.Value.GetInt32().ShouldBe(42);
    }

    [Fact]
    public async Task Error_samples_bypass_generation_and_cancelled_previews_do_not_run()
    {
        using var services = Services();
        var evaluator = new ExpressionEvaluator(services);
        var request = new ExpressionEvaluationRequest { Engine = "jsonata", PreviewProvider = Provider,
            Expression = "$fakeInt(9,1)", IsErrorInput = true,
            Input = "{\"Code\":\"original\",\"Message\":\"upstream failed\",\"Category\":\"test\"}" };
        var result = await evaluator.EvaluateAsync(request);
        result.Error.ShouldBeNull(result.Details);
        result.Result!.Value.GetProperty("Code").GetString().ShouldBe("original");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(() => evaluator.EvaluateAsync(request, cancelled.Token));
    }

    [Fact]
    public void Portable_catalog_retains_preview_metadata_and_older_fields_keep_their_defaults()
    {
        var catalog = new SourcesPlugin().Description;
        var json = JsonSerializer.Serialize(new[] { catalog }, PluginJsonContext.Default.PluginDescriptionArray);
        var copy = JsonSerializer.Deserialize(json, PluginJsonContext.Default.PluginDescriptionArray)!.Single();
        var expression = copy.Components.Single(c => c.Palette.ComponentType == "dataGenerator").Inspector.Sections
            .SelectMany(s => s.Options).Single(o => o.Name == "Expression");
        expression.PreviewProvider.ShouldBe(Provider); expression.PreviewFields.ShouldBe(new[] { "Locale" });
        expression.PreviewExpression.ShouldBeTrue();
        var legacy = JsonSerializer.Deserialize<ExpressionEvaluationRequest>("{\"Engine\":\"jsonata\",\"Expression\":\"42\"}");
        legacy!.PreviewProvider.ShouldBeNull(); legacy.PreviewSettings.ShouldBeEmpty();
    }
}
