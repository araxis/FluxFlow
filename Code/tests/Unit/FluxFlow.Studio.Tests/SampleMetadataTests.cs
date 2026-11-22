using System.Text.Json;
using System.Text.Json.Nodes;
using FluxFlow.Studio.UI.Features.Designer.Components.Expressions;
using FluxFlow.Authoring.Evaluation;
using FluxFlow.Expressions.Jsonata;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Studio.Tests;

public sealed class SampleMetadataTests
{
    [Theory]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("null")]
    public void Headers_protect_the_root_object(string edit)
    {
        var guard = new SampleMetadataEditGuard();
        guard.Initialize(Metadata());
        guard.TryAccept(Metadata().Replace("\"$headers\":{}", $"\"$headers\":{edit}")).ShouldBeFalse();
        guard.TryAccept(Metadata("{\"A\":2}")).ShouldBeTrue();
    }

    [Fact]
    public void Headers_allow_typing_deleting_properties_and_restoring_previous_text()
    {
        var guard = new SampleMetadataEditGuard();
        guard.Initialize(Metadata());
        foreach (var text in new[] { "{\"}", "{\"A\"}", "{\"A\":}", "{\"A\":1}", "{}", "{\"A\":1}" })
            guard.TryAccept(Metadata(text)).ShouldBeTrue(text);
    }

    [Fact]
    public void Metadata_rejects_added_deleted_renamed_and_duplicate_properties()
    {
        var original = Metadata();
        var guard = new SampleMetadataEditGuard();
        guard.Initialize(original);
        var metadata = JsonNode.Parse(original)!.AsObject();
        var edit = metadata;
        edit.Add("Extra", 1);
        guard.TryAccept(metadata.ToJsonString()).ShouldBeFalse();
        edit.Remove("Extra");
        edit.Remove("$messageId");
        guard.TryAccept(metadata.ToJsonString()).ShouldBeFalse();
        edit.Add("$MessageId", "changed");
        guard.TryAccept(metadata.ToJsonString()).ShouldBeFalse();
        guard.TryAccept(original.Replace("\"$messageId\"", "\"$traceId\"")).ShouldBeFalse();
        guard.TryAccept(original.Replace("$messageId", "$messageI")).ShouldBeFalse();
        guard.TryAccept(original.Replace("$headers", "$header")).ShouldBeFalse();
        guard.TryAccept(original).ShouldBeTrue();
    }

    [Fact]
    public void Metadata_allows_unfinished_values_formatting_and_unicode_without_unlocking_names()
    {
        var original = Metadata();
        var guard = new SampleMetadataEditGuard();
        guard.Initialize(original);
        var empty = original.Replace("\"id\"", "");
        guard.TryAccept(empty).ShouldBeTrue();
        guard.TryAccept(empty.Replace("$messageId", "Other")).ShouldBeFalse();
        var unfinished = original.Replace("\"id\"", "\"new");
        guard.TryAccept(unfinished).ShouldBeTrue();
        guard.TryAccept(original.Replace("\"id\"", "\"new\"")).ShouldBeTrue();
        guard.TryAccept(JsonNode.Parse(original)!.ToJsonString(new JsonSerializerOptions { WriteIndented = true })).ShouldBeTrue();
        guard.TryAccept(original).ShouldBeTrue();
    }

    [Fact]
    public void Metadata_does_not_accept_a_property_injected_through_a_value()
    {
        var guard = new SampleMetadataEditGuard();
        guard.Initialize(Metadata());
        guard.TryAccept(Metadata().Replace("\"id\"", "\"id\",\"Extra\":2")).ShouldBeFalse();
        guard.TryAccept(Metadata().Replace("\"id\"", "\"new\"")).ShouldBeTrue();
    }

    [Fact]
    public void Combined_document_shell_is_fixed_but_header_contents_can_be_cleared()
    {
        var guard = new SampleMetadataEditGuard();
        guard.Initialize(Metadata("{\"A\":{\"B\":1}}"));
        foreach (var text in new[] { "", "{}", "null", Metadata()[1..], Metadata()[..^1], Metadata().Replace("\"$headers\":{}", "") })
            guard.TryAccept(text).ShouldBeFalse(text);
        guard.TryAccept(Metadata()).ShouldBeTrue();
    }

    private static string Metadata(string headers = "{}") =>
        "{\"$traceId\":\"😀 فارسی\",\"$messageId\":\"id\",\"$causationId\":null,\"$correlationId\":null,\"$timestamp\":\"2026-10-23T12:00:00+00:00\",\"$headers\":" + headers + "}";

    [Fact]
    public void Editor_projection_round_trips_native_metadata_and_headers_without_renaming_header_keys()
    {
        var request = new ExpressionEvaluationRequest { Headers = "{\"PascalCase\":{\"A.B\":true},\"Null\":null}" };
        var editor = SampleMetadataJson.Create(request.Context, request.Headers);
        editor.ContainsKey("Context").ShouldBeFalse();
        var (context, headers) = SampleMetadataJson.Read(editor.ToJsonString());
        JsonNode.DeepEquals(JsonNode.Parse(context), JsonNode.Parse(request.Context)).ShouldBeTrue();
        JsonNode.DeepEquals(JsonNode.Parse(headers), JsonNode.Parse(request.Headers)).ShouldBeTrue();
    }

    [Theory]
    [InlineData("$traceId")]
    [InlineData("$messageId")]
    [InlineData("$causationId")]
    [InlineData("$correlationId")]
    [InlineData("$timestamp")]
    [InlineData("$headers")]
    public async Task Displayed_variables_evaluate_through_native_bindings(string variable)
    {
        using var services = new ServiceCollection().AddJsonataExpressionEngine().BuildServiceProvider();
        var request = new ExpressionEvaluationRequest { Engine = "jsonata", Expression = variable };
        var editor = SampleMetadataJson.Create(request.Context, "{\"A\":{\"B\":7}}");
        editor["$messageId"] = "sample-message";
        editor["$correlationId"] = "sample-correlation";
        editor["$causationId"] = "sample-cause";
        var (context, headers) = SampleMetadataJson.Read(editor.ToJsonString());
        var result = await new ExpressionEvaluator(services).EvaluateAsync(request with { Context = context, Headers = headers });
        result.Error.ShouldBeNull();
        var pascalVariable = "$" + char.ToUpperInvariant(variable[1]) + variable[2..];
        var pascalResult = await new ExpressionEvaluator(services).EvaluateAsync(request with
        {
            Expression = pascalVariable, Context = context, Headers = headers
        });
        pascalResult.Error.ShouldBeNull();
        pascalResult.Result!.Value.GetRawText().ShouldBe(result.Result!.Value.GetRawText());
        var actual = JsonNode.Parse(result.Result!.Value.GetRawText());
        if (variable == "$timestamp")
            actual!.GetValue<DateTimeOffset>().ShouldBe(editor[variable]!.GetValue<DateTimeOffset>());
        else
            JsonNode.DeepEquals(actual, editor[variable]).ShouldBeTrue();
    }

    [Theory]
    [InlineData("42")]
    [InlineData("\"hello\"")]
    [InlineData("null")]
    [InlineData("true")]
    [InlineData("{\"Nested\":[1,2]}")]
    public void Dotted_header_paths_merge_objects_and_preserve_typed_values(string value)
    {
        var original = JsonNode.Parse("{\"A\":{\"Existing\":7},\"a\":false}")!.AsObject();
        var result = SampleHeaderPath.Add(original, "A.B.C", JsonNode.Parse(value));
        result["A"]!["B"]!["C"]?.ToJsonString().ShouldBe(JsonNode.Parse(value)?.ToJsonString());
        result["A"]!["Existing"]!.GetValue<int>().ShouldBe(7);
        result["a"]!.GetValue<bool>().ShouldBeFalse();
        original.ToJsonString().ShouldBe("{\"A\":{\"Existing\":7},\"a\":false}");
    }

    [Theory]
    [InlineData("{\"A\":1}", "A.B")]
    [InlineData("{\"A\":null}", "A.B")]
    [InlineData("{\"A\":[]}", "A.B")]
    [InlineData("{\"A\":{\"B\":1}}", "A.B")]
    [InlineData("{}", "A..B")]
    [InlineData("{}", ".A")]
    [InlineData("{}", "A.")]
    public void Conflicting_or_empty_path_segments_leave_headers_unchanged(string json, string path)
    {
        var headers = JsonNode.Parse(json)!.AsObject();
        Should.Throw<JsonException>(() => SampleHeaderPath.Add(headers, path, JsonValue.Create(2)));
        headers.ToJsonString().ShouldBe(json);
    }
}
