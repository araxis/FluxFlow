using System.Text.Json.Nodes;
using FluxFlow.Studio.UI.Features.Designer;
using Shouldly;
using Xunit;

namespace FluxFlow.Studio.Tests;

public sealed class DesignerConfigurationValidatorTests
{
    private static IReadOnlyList<ValidationMessageModel> Validate(string json, string sectionPath = "") =>
        DesignerConfigurationValidator.Validate(json, sectionPath, StudioTestServices.CreateGraph().PluginValidators);

    [Fact]
    public void Supported_registrations_accept_inline_configuration_without_resolving_expression_engines()
    {
        const string json = """
        {"Workflows":{"main":{
          "clock":{"Type":"scheduler","Interval":"00:00:01"},
          "source":{"Type":"source","Values":"[1,\"1\",null]"},
          "map":{"Type":"mapper","Engine":"unavailable","Expression":"file:does-not-exist.jsonata"},
          "batch":{"Type":"batch","BatchSize":2,"MaxWait":"00:00:01"},
          "gate":{"Type":"gate"}
        }}}
        """;
        var messages = Validate(json);
        messages.ShouldHaveSingleItem().Address!.Value.ShouldBe("main.map");
        messages[0].Severity.ShouldBe(ValidationSeverity.Information);
        messages[0].Message.ShouldContain("Not validated");
    }

    [Fact]
    public void Registration_failures_are_located_and_do_not_stop_checks_in_other_workflows()
    {
        const string json = """
        {"Logging":{"Keep":true},"FluxFlow":{"Workflows":{
          "a":{"clock":{"Type":"scheduler"}},
          "b":{"map":{"Type":"mapper","Expression":"$"}}
        }}}
        """;
        var messages = Validate(json, "FluxFlow");
        messages.Count.ShouldBe(2);
        messages.ShouldAllBe(item => item.Severity == ValidationSeverity.Error);
        messages.Select(item => item.Address!.Value).ShouldBe(["a.clock", "b.map"]);
        messages[0].Message.ShouldContain("Specify exactly one of Interval, Cron or At.");
        messages[1].Path.ShouldBe("FluxFlow:Workflows:b:map");
    }

    [Theory]
    [InlineData("file:missing.json")]
    [InlineData("./missing.json")]
    [InlineData("missing.json")]
    public void External_source_files_are_not_opened_or_reported_as_invalid(string values)
    {
        var json = new JsonObject { ["Workflows"] = new JsonObject { ["main"] = new JsonObject
            { ["source"] = new JsonObject { ["Type"] = "source", ["Values"] = values } } } };
        var messages = Validate(json.ToJsonString());
        messages.ShouldHaveSingleItem().Severity.ShouldBe(ValidationSeverity.Warning);
        messages[0].Message.ShouldContain("external file");
    }

    [Theory]
    [InlineData("source", "Values", "./unavailable.json")]
    [InlineData("reducer", "InitialState", "file:unavailable.json")]
    public void External_json_does_not_hide_errors_in_other_fields(string type, string key, string file)
    {
        var node = new JsonObject { ["Type"] = type, [key] = file, ["OutputCapacity"] = 0 };
        if (type == "reducer") { node["Engine"] = "custom"; node["Reduce"] = "$input"; node["AreEqual"] = "false"; }
        var graph = StudioTestServices.CreateGraph();
        graph.LoadJson(new JsonObject { ["Workflows"] = new JsonObject { ["main"] = new JsonObject { ["item"] = node } } }.ToJsonString());
        graph.ResetHistory();
        var before = graph.ToProjectJson();
        var messages = Validate(graph.ToJson());
        messages.ShouldContain(item => item.Severity == ValidationSeverity.Warning && item.Message.Contains("external file"));
        messages.Single(item => item.Severity == ValidationSeverity.Error).Message.ShouldContain("OutputCapacity");
        graph.ToProjectJson().ShouldBe(before);
        graph.CanUndo.ShouldBeFalse();
    }

    [Theory]
    [InlineData("inline: [broken")]
    [InlineData("[broken")]
    [InlineData("123")]
    public void Invalid_inline_source_values_are_native_registration_errors(string values)
    {
        var json = new JsonObject { ["Workflows"] = new JsonObject { ["main"] = new JsonObject
            { ["source"] = new JsonObject { ["Type"] = "source", ["Values"] = values } } } };
        Validate(json.ToJsonString()).ShouldHaveSingleItem().Severity.ShouldBe(ValidationSeverity.Error);
    }

    [Fact]
    public void Unavailable_types_and_resources_are_explicitly_unvalidated_and_document_is_preserved()
    {
        const string json = """
        {"FluxFlow":{"Resources":{"Custom":{"Type":"external","Values":[1,"1",null]}},"Workflows":{
          "main":{"map":{"type":"mapper","Engine":"custom","Expression":"$","Unknown":{"ExactCase":false}},
          "custom":{"Type":"custom"}},"other":{}}},"Logging":{"Keep":true}}
        """;
        var graph = StudioTestServices.CreateGraph();
        graph.LoadJson(json, "FluxFlow");
        graph.ResetHistory();
        var messages = Validate(graph.ToJson(), graph.SectionPath);
        messages.Count.ShouldBe(3);
        messages.ShouldNotContain(item => item.Severity == ValidationSeverity.Error);
        messages.ShouldContain(item => item.Path == "FluxFlow:Resources");
        messages.ShouldContain(item => item.Address != null && item.Address.Value == "main.custom");
        JsonNode.DeepEquals(JsonNode.Parse(json), JsonNode.Parse(graph.ToJson())).ShouldBeTrue();
        graph.CanUndo.ShouldBeFalse();
    }

    [Theory]
    [InlineData("{\"Type\":\"gate\",\"Output\":\"bad\"}", true)]
    [InlineData("{\"Type\":\"gate\",\"UnexpectedSetting\":true}", false)]
    public void Native_registration_rules_check_links_without_rejecting_preserved_unknown_settings(string component, bool hasError)
    {
        var json = new JsonObject { ["Workflows"] = new JsonObject { ["main"] = new JsonObject
            { ["gate"] = JsonNode.Parse(component) } } };
        var results = Validate(json.ToJsonString());
        if (!hasError) { results.ShouldBeEmpty(); return; }
        var result = results.ShouldHaveSingleItem();
        result.Severity.ShouldBe(ValidationSeverity.Error);
        result.Address!.Value.ShouldBe("main.gate");
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"Workflows\":{\"main\":{\"bad\":{\"Type\":\"gate\",\"type\":\"gate\"}}}}")]
    public void Malformed_documents_return_diagnostics(string json)
    {
        Validate(json).ShouldHaveSingleItem().Severity.ShouldBe(ValidationSeverity.Error);
    }
}
