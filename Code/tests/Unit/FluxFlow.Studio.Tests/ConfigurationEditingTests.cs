using System.Text.Json;
using System.Text.Json.Nodes;
using FluxFlow.Application.Addressing;
using Shouldly;
using Xunit;

namespace FluxFlow.Studio.Tests;

public sealed class ConfigurationEditingTests
{
    internal const string Original = """
        {"Host":{"KeepCase":true},"Application":{"Resources":{"device":{"Type":"external","Keep":[null,1234567890123456789]}},"Workflows":{
        "main":{"source":{"Type":"mapper","Engine":"jsonata","expression":"$","Custom":{"UPPER":42},"Nullable":1}},
        "other":{"sink":{"Type":"batch","BatchSize":2,"MaxWait":"00:00:01"},"unknown":{"Type":"third-party","Extra":true}}}}}
        """;

    internal static JsonElement Json(string value) => JsonSerializer.Deserialize<JsonElement>(value);
    internal static ComponentAddress Address(string value) => ComponentAddress.Parse(value);

    internal static ConfigurationEdit[] Batch() =>
    [
        new AddComponentEdit(Address("main.map"), "mapper", new Dictionary<string, JsonElement> { ["Engine"] = Json("\"jsonata\"") }),
        new ConfigureComponentEdit(Address("main.map"), new Dictionary<string, JsonElement?> { ["Expression"] = Json("\"Reading * 2\"") }),
        new ConnectPortsEdit(PortAddress.Parse("main.source.Output"), PortAddress.Parse("main.map.Input")),
        new ConnectPortsEdit(PortAddress.Parse("main.map.Output"), PortAddress.Parse("other.sink.Input"), "$IsError", "jsonata", true)
    ];

    [Fact]
    public void Headless_batch_preserves_unrelated_configuration_and_leaves_original_untouched()
    {
        var document = new DesignerConfiguration(Original, "Application");
        var before = document.ToJson();
        var candidate = document.PrepareEdits(Batch());
        document.ToJson().ShouldBe(before);
        var oldRoot = JsonNode.Parse(before)!;
        var result = JsonNode.Parse(candidate.ToJson())!;
        JsonNode.DeepEquals(result["Host"], oldRoot["Host"]).ShouldBeTrue();
        JsonNode.DeepEquals(result["Application"]!["Resources"], oldRoot["Application"]!["Resources"]).ShouldBeTrue();
        JsonNode.DeepEquals(result["Application"]!["Workflows"]!["other"], oldRoot["Application"]!["Workflows"]!["other"]).ShouldBeTrue();
        var map = candidate.Nodes("main").Single(node => node.Address.ComponentName == "map");
        map.Values["Expression"].GetString().ShouldBe("Reading * 2");
        var error = result["Application"]!["Workflows"]!["main"]!["map"]!["Error"]!;
        error["Address"]!.GetValue<string>().ShouldBe("other.sink.Input");
        error["Condition"]!.GetValue<string>().ShouldBe("$IsError");
        new DesignerConfiguration(candidate.ToApplicationJson()).Nodes("main").Count.ShouldBe(2);
    }

    [Fact]
    public void Configure_preserves_property_spelling_and_distinguishes_removal_from_json_null()
    {
        var document = new DesignerConfiguration(Original, "Application");
        var candidate = document.PrepareEdits([new ConfigureComponentEdit(Address("main.source"),
            new Dictionary<string, JsonElement?> { ["Expression"] = Json("\"Reading\""), ["Custom"] = null, ["Nullable"] = Json("null") })]);
        var node = candidate.Nodes("main").Single();
        node.Values.ContainsKey("Expression").ShouldBeFalse();
        node.Values["expression"].GetString().ShouldBe("Reading");
        node.Values.ContainsKey("Custom").ShouldBeFalse();
        node.Values["Nullable"].ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("missing")]
    [InlineData("engine")]
    [InlineData("type")]
    [InlineData("error")]
    public void A_late_failure_leaves_the_whole_document_unchanged(string failure)
    {
        var document = new DesignerConfiguration(Original, "Application");
        var before = document.ToJson();
        ConfigurationEdit invalid = failure switch
        {
            "duplicate" => new AddComponentEdit(Address("main.SOURCE"), "mapper"),
            "missing" => new ConnectPortsEdit(PortAddress.Parse("main.map.Output"), PortAddress.Parse("other.missing.Input")),
            "engine" => new ConnectPortsEdit(PortAddress.Parse("main.map.Output"), PortAddress.Parse("other.sink.Input"), Engine: "jsonata"),
            "type" => new ConfigureComponentEdit(Address("main.map"), new Dictionary<string, JsonElement?> { ["Type"] = Json("\"source\"") }),
            _ => new ConnectPortsEdit(PortAddress.Parse("main.map.Events"), PortAddress.Parse("other.sink.Input"), IsError: true)
        };
        Should.Throw<Exception>(() => document.PrepareEdits([.. Batch(), invalid]));
        document.ToJson().ShouldBe(before);
    }

    [Fact]
    public void Can_create_an_application_without_Studio_or_component_descriptions()
    {
        var document = new DesignerConfiguration("{\"Workflows\":{}}");
        var candidate = document.PrepareEdits([
            new AddComponentEdit(Address("sensors.input"), "custom.receiver"),
            new AddComponentEdit(Address("sensors.output"), "custom.sender"),
            new ConnectPortsEdit(PortAddress.Parse("sensors.input.Output"), PortAddress.Parse("sensors.output.Input"))]);
        candidate.Nodes("sensors").Count.ShouldBe(2);
        document.WorkflowNames.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("configure")]
    [InlineData("source")]
    [InlineData("target")]
    public void Workflow_resources_cannot_be_addressed_as_a_component(string operation)
    {
        var document = new DesignerConfiguration("""
            {"Workflows":{"main":{"Resources":{"device":{"Type":"custom"}},"map":{"Type":"mapper"}}}}
            """);
        var before = document.ToJson();
        var resources = Address("main.Resources");
        var map = Address("main.map");
        ConfigurationEdit edit = operation switch
        {
            "configure" => new ConfigureComponentEdit(resources, new Dictionary<string, JsonElement?> { ["device"] = null }),
            "source" => new ConnectPortsEdit(resources.Port("Output"), map.Port("Input")),
            _ => new ConnectPortsEdit(map.Port("Output"), resources.Port("Input"))
        };
        Should.Throw<ArgumentException>(() => document.PrepareEdits([edit]));
        document.ToJson().ShouldBe(before);
    }
}
