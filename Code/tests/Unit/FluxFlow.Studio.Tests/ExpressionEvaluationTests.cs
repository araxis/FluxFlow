using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluxFlow.Authoring.Evaluation;
using FluxFlow.Authoring;
using FluxFlow.Expressions.CSharp;
using FluxFlow.Expressions.Jsonata;
using FluxFlow.StudioWebHost;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Studio.Tests;

public sealed class ExpressionEvaluationTests
{
    [Theory]
    [InlineData("jsonata", ExpressionResultKind.String, "inline: $input.Key", "\"CaseSensitive\"")]
    [InlineData("csharp", ExpressionResultKind.String, "inline: (string)Input.Key", "\"CaseSensitive\"")]
    [InlineData("jsonata", ExpressionResultKind.Boolean, "inline: $input.Count > 2", "true")]
    [InlineData("csharp", ExpressionResultKind.Boolean, "inline: Input.Count > 2", "true")]
    [InlineData("jsonata", ExpressionResultKind.Boolean, "base64:dHJ1ZQ==", "true")]
    public async Task Component_previews_use_native_return_types_and_source_reading(string engine, ExpressionResultKind kind, string source, string expected)
    {
        using var services = Engines();
        var request = new ExpressionEvaluationRequest
        {
            Engine = engine, ResultKind = kind, Expression = source,
            Input = "{\"Key\":\"CaseSensitive\",\"Count\":3}"
        };
        var original = JsonSerializer.Serialize(request, ExpressionEvaluationJsonContext.Default.ExpressionEvaluationRequest);
        var result = await new ExpressionEvaluator(services).EvaluateAsync(request);
        result.Error.ShouldBeNull();
        result.Result!.Value.GetRawText().ShouldBe(expected);
        JsonSerializer.Serialize(request, ExpressionEvaluationJsonContext.Default.ExpressionEvaluationRequest).ShouldBe(original);
    }

    [Theory]
    [InlineData(ExpressionResultKind.String)]
    [InlineData(ExpressionResultKind.Boolean)]
    public async Task Typed_previews_reject_wrong_results_and_do_not_read_files(ExpressionResultKind kind)
    {
        using var services = Engines();
        var evaluator = new ExpressionEvaluator(services);
        var request = new ExpressionEvaluationRequest { Engine = "jsonata", ResultKind = kind, Expression = "42" };
        var invalid = await evaluator.EvaluateAsync(request);
        invalid.Error.ShouldNotBeNullOrEmpty(); invalid.Result.ShouldBeNull();
        var file = await evaluator.EvaluateAsync(request with { Expression = "file:unopened.jsonata" });
        file.Stage.ShouldBe("Source"); file.Error!.ShouldContain("File evaluation is deferred");
        var noEngine = await evaluator.EvaluateAsync(request with { Engine = null });
        noEngine.Stage.ShouldBe("Engine");
    }

    [Fact]
    public async Task Retry_preview_distinguishes_error_envelopes_from_identically_shaped_values()
    {
        await using var host = StudioHost.Create([], builder => builder.WebHost.UseTestServer());
        await host.StartAsync();
        using var client = host.GetTestClient();
        var sample = new ExpressionEvaluationRequest
        {
            Engine = "jsonata", ResultKind = ExpressionResultKind.Boolean,
            Expression = "$message.IsError and $message.Error.IsTransient and $message.Error.Code = $headers.Expected",
            Input = """{"Code":"Busy","Message":"Try again","Category":"Service","IsTransient":true}""",
            Headers = """{"Expected":"Busy"}"""
        };
        foreach (var (error, input, expected) in new[] { (true, sample.Input, (bool?)true), (false, sample.Input, false),
            (true, sample.Input.Replace("true", "false"), false), (true, "{}", null) })
        {
            using var response = await client.PostAsJsonAsync("/api/expressions/evaluate", sample with { IsErrorInput = error, Input = input },
                ExpressionEvaluationJsonContext.Default.ExpressionEvaluationRequest);
            response.EnsureSuccessStatusCode();
            var result = (await response.Content.ReadFromJsonAsync(ExpressionEvaluationJsonContext.Default.ExpressionEvaluationResult))!;
            if (expected.HasValue) { result.Error.ShouldBeNull(); result.Result!.Value.GetBoolean().ShouldBe(expected.Value); }
            else { result.Error.ShouldNotBeNullOrEmpty(); result.Result.ShouldBeNull(); }
        }
    }

    [Fact]
    public async Task Web_preview_preserves_the_requested_result_contract()
    {
        await using var host = StudioHost.Create([], builder => builder.WebHost.UseTestServer());
        await host.StartAsync();
        using var client = host.GetTestClient();
        var request = new ExpressionEvaluationRequest { Engine = "jsonata", ResultKind = ExpressionResultKind.String, Expression = "42" };
        using var response = await client.PostAsJsonAsync("/api/expressions/evaluate", request, ExpressionEvaluationJsonContext.Default.ExpressionEvaluationRequest);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync(ExpressionEvaluationJsonContext.Default.ExpressionEvaluationResult);
        result!.Error.ShouldNotBeNullOrEmpty(); result.Result.ShouldBeNull();
    }

    [Theory]
    [InlineData("jsonata", "$input.Reading + $headers.Offset")]
    [InlineData("jsonata", "Reading + $headers.Offset")]
    [InlineData("jsonata", "$.Reading + $headers.Offset")]
    [InlineData("jsonata", "$Input.Reading + $Headers.Offset")]
    [InlineData("jsonata", "$Value.Reading + $Headers.Offset")]
    [InlineData("jsonata", "$Payload.Reading + $Headers.Offset")]
    [InlineData("csharp", "Input.Reading + Headers.Offset")]
    [InlineData("csharp", "input.Reading + headers.Offset")]
    public async Task Uses_native_message_values_and_preserves_the_request(string engine, string expression)
    {
        using var services = Engines();
        var request = new ExpressionEvaluationRequest
        {
            Engine = engine, Expression = expression,
            Input = "{\"Reading\":12}", Headers = "{\"Offset\":3}"
        };
        var original = JsonSerializer.Serialize(request, ExpressionEvaluationJsonContext.Default.ExpressionEvaluationRequest);
        var result = await new ExpressionEvaluator(services).EvaluateAsync(request);
        result.Error.ShouldBeNull();
        result.Result!.Value.GetInt32().ShouldBe(15);
        JsonSerializer.Serialize(request, ExpressionEvaluationJsonContext.Default.ExpressionEvaluationRequest).ShouldBe(original);
    }

    [Fact]
    public async Task Exposes_ids_timestamp_and_typed_headers_from_the_native_context()
    {
        using var services = Engines();
        var request = new ExpressionEvaluationRequest
        {
            Engine = "jsonata", Input = "null", Headers = "{\"Enabled\":true,\"Items\":[1,2]}",
            Expression = "{\"MessageId\":$messageId,\"TraceId\":$traceId,\"Timestamp\":$timestamp,\"Headers\":$headers,\"Value\":$input}"
        };
        var context = JsonNode.Parse(request.Context)!;
        var result = await new ExpressionEvaluator(services).EvaluateAsync(request);
        result.Error.ShouldBeNull();
        var json = result.Result!.Value;
        json.GetProperty("MessageId").GetString().ShouldBe(context["MessageId"]!.GetValue<string>());
        json.GetProperty("TraceId").GetString().ShouldBe(context["TraceId"]!.GetValue<string>());
        json.GetProperty("Timestamp").GetDateTimeOffset().ShouldBe(context["Timestamp"]!.GetValue<DateTimeOffset>());
        json.GetProperty("Headers").GetProperty("Enabled").GetBoolean().ShouldBeTrue();
        json.GetProperty("Headers").GetProperty("Items").GetArrayLength().ShouldBe(2);
        json.GetProperty("Value").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Conditions_use_the_host_default_and_require_a_boolean()
    {
        using var services = Engines();
        var evaluator = new ExpressionEvaluator(services);
        var request = new ExpressionEvaluationRequest { IsCondition = true, Expression = "$input > 10", Input = "12" };
        var result = await evaluator.EvaluateAsync(request);
        result.Error.ShouldBeNull();
        result.Engine.ShouldBe("jsonata");
        result.Result!.Value.GetBoolean().ShouldBeTrue();
        var nonBoolean = await evaluator.EvaluateAsync(request with { Expression = "42" });
        nonBoolean.Error.ShouldNotBeNullOrEmpty();
        nonBoolean.Result.ShouldBeNull();
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("JSONATA", true)]
    public async Task Missing_explicit_engines_do_not_fall_back(string? engine, bool condition)
    {
        using var services = Engines();
        var result = await new ExpressionEvaluator(services).EvaluateAsync(new() { Engine = engine, IsCondition = condition, Expression = "true" });
        result.Stage.ShouldBe("Engine");
        result.Error.ShouldNotBeNullOrEmpty();
    }

    [Theory]
    [InlineData("inline: {\"PascalCase\":42}")]
    [InlineData("base64:eyJQYXNjYWxDYXNlIjo0Mn0=")]
    public async Task Uses_native_inline_and_base64_source_reading(string expression)
    {
        using var services = Engines();
        var result = await new ExpressionEvaluator(services).EvaluateAsync(new() { Engine = "jsonata", Expression = expression });
        result.Error.ShouldBeNull();
        result.Result!.Value.GetProperty("PascalCase").GetInt32().ShouldBe(42);
    }

    [Theory]
    [InlineData("file:sample.jsonata")]
    [InlineData("./sample")]
    [InlineData("../sample")]
    [InlineData("sample.jsonata")]
    [InlineData("C:\\sample.cs")]
    public async Task File_sources_are_rejected_before_compilation(string expression)
    {
        using var services = Engines();
        var result = await new ExpressionEvaluator(services).EvaluateAsync(new() { Engine = "jsonata", Expression = expression });
        result.Stage.ShouldBe("Source");
        result.Error!.ShouldContain("File evaluation is deferred");
    }

    [Theory]
    [InlineData("Input", "{")]
    [InlineData("Headers", "[]")]
    [InlineData("Context", "{\"Value\":1}")]
    [InlineData("Context", "{\"MessageId\":\"invalid\"}")]
    public async Task Invalid_sample_data_reports_its_stage_and_original_exception(string field, string value)
    {
        using var services = Engines();
        var request = new ExpressionEvaluationRequest { Engine = "jsonata", Expression = "$" };
        request = field switch { "Input" => request with { Input = value }, "Headers" => request with { Headers = value }, _ => request with { Context = value } };
        var result = await new ExpressionEvaluator(services).EvaluateAsync(request);
        result.Stage.ShouldBe(field);
        result.Error.ShouldNotBeNullOrEmpty();
        result.Details!.ShouldContain(result.Error!);
        result.Result.ShouldBeNull();
    }

    [Fact]
    public async Task Null_is_a_result_but_undefined_and_invalid_syntax_are_errors()
    {
        using var services = Engines();
        var evaluator = new ExpressionEvaluator(services);
        var request = new ExpressionEvaluationRequest { Engine = "jsonata", Expression = "null" };
        var result = await evaluator.EvaluateAsync(request);
        result.Error.ShouldBeNull();
        result.Result!.Value.ValueKind.ShouldBe(JsonValueKind.Null);
        var undefined = await evaluator.EvaluateAsync(request with { Expression = "$missing" });
        undefined.Stage.ShouldBe("Evaluate");
        undefined.Error.ShouldNotBeNullOrEmpty();
        var syntax = await evaluator.EvaluateAsync(request with { Expression = "{" });
        syntax.Stage.ShouldBe("Compile");
        syntax.Error.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task Input_properties_do_not_create_dollar_variables()
    {
        using var services = Engines();
        var result = await new ExpressionEvaluator(services).EvaluateAsync(new()
        {
            Engine = "jsonata", Expression = "$exists($Reading)", Input = "{\"Reading\":12}"
        });
        result.Error.ShouldBeNull();
        result.Result!.Value.GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task Canceled_requests_do_not_begin_evaluation()
    {
        using var services = Engines();
        await Should.ThrowAsync<OperationCanceledException>(() => new ExpressionEvaluator(services)
            .EvaluateAsync(new() { Engine = "jsonata", Expression = "1" }, new CancellationToken(true)));
    }

    [Fact]
    public async Task Web_endpoint_returns_diagnostics_then_accepts_a_corrected_sample()
    {
        await using var host = StudioHost.Create([], builder => builder.WebHost.UseTestServer());
        await host.StartAsync();
        using var client = host.GetTestClient();
        var request = new ExpressionEvaluationRequest { Engine = "jsonata", Expression = "$input.Reading * 2", Input = "{" };
        using var failedResponse = await client.PostAsJsonAsync("/api/expressions/evaluate", request, ExpressionEvaluationJsonContext.Default.ExpressionEvaluationRequest);
        failedResponse.EnsureSuccessStatusCode();
        var failed = await failedResponse.Content.ReadFromJsonAsync(ExpressionEvaluationJsonContext.Default.ExpressionEvaluationResult);
        failed!.Stage.ShouldBe("Input");
        failed.Error.ShouldNotBeNullOrEmpty();
        using var successResponse = await client.PostAsJsonAsync("/api/expressions/evaluate", request with { Input = "{\"Reading\":21}" }, ExpressionEvaluationJsonContext.Default.ExpressionEvaluationRequest);
        successResponse.EnsureSuccessStatusCode();
        var text = await successResponse.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(text);
        json.RootElement.GetProperty("Result").GetInt32().ShouldBe(42);
        json.RootElement.GetProperty("Error").ValueKind.ShouldBe(JsonValueKind.Null);
        using var nullResponse = await client.PostAsJsonAsync("/api/expressions/evaluate", request with { Input = "{}", Expression = "null" }, ExpressionEvaluationJsonContext.Default.ExpressionEvaluationRequest);
        var nullResult = await nullResponse.Content.ReadFromJsonAsync(ExpressionEvaluationJsonContext.Default.ExpressionEvaluationResult);
        nullResult!.Error.ShouldBeNull();
        nullResult.Result.ShouldBeNull();
        await host.StopAsync();
    }

    private static ServiceProvider Engines() => new ServiceCollection()
        .AddJsonataExpressionEngine(useAsDefault: true).AddCSharpExpressionEngine().BuildServiceProvider();

    [Theory]
    [InlineData("add")]
    [InlineData("remove")]
    [InlineData("rename")]
    [InlineData("case")]
    public async Task Context_structure_is_fixed(string change)
    {
        using var services = Engines();
        var request = new ExpressionEvaluationRequest { Engine = "jsonata", Expression = "$messageId" };
        var context = JsonNode.Parse(request.Context)!.AsObject();
        context.Select(item => item.Key).ShouldBe(ExpressionEvaluationRequest.ContextProperties);
        if (change == "add") context["Extra"] = "sample";
        else
        {
            var value = context["MessageId"]!.DeepClone();
            context.Remove("MessageId");
            if (change != "remove") context[change == "case" ? "messageId" : "Name"] = value;
        }
        var result = await new ExpressionEvaluator(services).EvaluateAsync(request with { Context = context.ToJsonString() });
        result.Stage.ShouldBe("Context");
        result.Error.ShouldNotBeNullOrEmpty();
        result.Result.ShouldBeNull();
    }

    [Fact]
    public async Task Fixed_context_allows_test_values_and_retains_native_value_validation()
    {
        using var services = Engines();
        var evaluator = new ExpressionEvaluator(services);
        var request = new ExpressionEvaluationRequest
        {
            Engine = "jsonata", Expression = "{\"Id\":$messageId,\"Correlation\":$correlationId,\"Timestamp\":$timestamp}"
        };
        var context = JsonNode.Parse(request.Context)!.AsObject();
        var id = Guid.NewGuid().ToString("N");
        context["MessageId"] = id;
        context["CorrelationId"] = id;
        context["Timestamp"] = "2026-10-23T12:34:56+02:00";
        var result = await evaluator.EvaluateAsync(request with { Context = context.ToJsonString() });
        result.Error.ShouldBeNull();
        result.Result!.Value.GetProperty("Id").GetString().ShouldBe(id);
        result.Result.Value.GetProperty("Correlation").GetString().ShouldBe(id);
        result.Result.Value.GetProperty("Timestamp").GetDateTimeOffset().ShouldBe(DateTimeOffset.Parse("2026-10-23T12:34:56+02:00"));
        context["Timestamp"] = "not-a-timestamp";
        var invalid = await evaluator.EvaluateAsync(request with { Context = context.ToJsonString() });
        invalid.Stage.ShouldBe("Context");
        invalid.Error.ShouldNotBeNullOrEmpty();
    }
}
