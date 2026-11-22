using System.Text.Json.Nodes;
using FluxFlow.Plugins.Http.WireMock;
using Shouldly;
using Xunit;

namespace FluxFlow.Studio.Tests;

public sealed class WireMockEndpointDraftTests
{
    [Fact]
    public void Visual_edits_preserve_unknown_mapping_fields_and_source()
    {
        var source = (JsonObject)JsonNode.Parse("""{"Guid":"keep","Priority":8,"Request":{"Path":"/old","Methods":["GET"],"Headers":{"Accept":{"Matchers":[{"Name":"ExactMatcher","Pattern":"x"}]}}},"Response":{"StatusCode":201,"BodyAsJson":{"KeepCase":1},"UseTransformer":true,"Headers":{"X-Keep":"yes"},"Delay":100},"Webhook":{"Keep":true}}""")!;
        var before = source.ToJsonString();
        var draft = new WireMockEndpointDraft(source) { Path = "/new", Method = "POST", Status = 202, Body = "{\"KeepCase\":2}" };
        draft.CanEdit.ShouldBeTrue();
        draft.Headers.Add(new() { Name = "Content-Type", Value = "application/json" });
        var result = draft.Apply();
        result["Request"]!["Path"]!.GetValue<string>().ShouldBe("/new");
        result["Request"]!["Methods"]![0]!.GetValue<string>().ShouldBe("POST");
        result["Response"]!["StatusCode"]!.GetValue<int>().ShouldBe(202);
        result["Response"]!["BodyAsJson"]!["KeepCase"]!.GetValue<int>().ShouldBe(2);
        result["Response"]!["Headers"]!["X-Keep"]!.GetValue<string>().ShouldBe("yes");
        result["Response"]!["Delay"]!.GetValue<int>().ShouldBe(100);
        result["Response"]!["UseTransformer"]!.GetValue<bool>().ShouldBeTrue();
        JsonNode.DeepEquals(result["Request"]!["Headers"], source["Request"]!["Headers"]).ShouldBeTrue();
        JsonNode.DeepEquals(result["Webhook"], source["Webhook"]).ShouldBeTrue();
        result["Priority"]!.GetValue<int>().ShouldBe(8);
        source.ToJsonString().ShouldBe(before);
    }

    [Theory]
    [InlineData("""{"Request":{},"Response":{}}""")]
    [InlineData("""{"Request":{"Path":"/","Methods":["GET"]},"Response":{"BodyAsJson":null,"Headers":{}}}""")]
    [InlineData("""{"Request":{"Path":{"Matchers":[]},"Methods":["POST","PUT"]},"Response":{"BodyAsFile":"file.json"}}""")]
    [InlineData("""{"Request":{"Path":"/"},"Response":{"Headers":{"Set-Cookie":["A=1","B=2"]}}}""")]
    public void Opening_a_mapping_does_not_normalize_or_drop_fields(string json)
    {
        var source = (JsonObject)JsonNode.Parse(json)!;
        JsonNode.DeepEquals(source, new WireMockEndpointDraft(source).Apply()).ShouldBeTrue();
    }

    [Fact]
    public void Complex_mappings_are_reserved_for_advanced_editing()
    {
        var source = (JsonObject)JsonNode.Parse("""{"Request":{"Path":{"Matchers":[{"Name":"RegexMatcher","Pattern":".*"}]}},"Response":{"Body":"test"}}""")!;
        var draft = new WireMockEndpointDraft(source) { Path = "/replacement" };
        draft.CanEdit.ShouldBeFalse();
        JsonNode.DeepEquals(draft.Apply(), source).ShouldBeTrue();
    }

    [Fact]
    public void Body_format_and_any_method_changes_remove_only_the_replaced_options()
    {
        var draft = new WireMockEndpointDraft((JsonObject)JsonNode.Parse("""{"Request":{"Path":"/","Methods":["GET"]},"Response":{"BodyAsJson":{"A":1},"UseTransformer":true}}""")!)
        { Method = "*", JsonBody = false, Body = "hello" };
        var result = draft.Apply();
        ((JsonObject)result["Request"]!).ContainsKey("Methods").ShouldBeFalse();
        ((JsonObject)result["Response"]!).ContainsKey("BodyAsJson").ShouldBeFalse();
        result["Response"]!["Body"]!.GetValue<string>().ShouldBe("hello");
        result["Response"]!["UseTransformer"]!.GetValue<bool>().ShouldBeTrue();
    }

    [Theory]
    [InlineData("body")]
    [InlineData("path")]
    [InlineData("status")]
    [InlineData("headers")]
    public void Invalid_visual_edits_do_not_modify_the_source(string field)
    {
        var source = (JsonObject)JsonNode.Parse("""{"Request":{"Path":"/"},"Response":{}}""")!;
        var before = source.ToJsonString();
        var draft = new WireMockEndpointDraft(source);
        if (field == "body") { draft.JsonBody = true; draft.Body = "{"; }
        if (field == "path") draft.Path = "bad path";
        if (field == "status") draft.Status = 600;
        if (field == "headers") { draft.Headers.Add(new() { Name = "X-Test" }); draft.Headers.Add(new() { Name = "x-test" }); }
        Should.Throw<Exception>(() => draft.Apply());
        source.ToJsonString().ShouldBe(before);
    }
}
