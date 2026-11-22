using System.Text.Json;
using System.Text.Json.Nodes;
using FluxFlow.Application.Addressing;
using FluxFlow.Authoring.Definitions;
using FluxFlow.Studio.UI.Features.Designer.Canvas;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Studio.Tests;

public sealed class ComponentPaletteTests
{
    [Fact]
    public void Zoom_to_fit_waits_for_a_newly_selected_node_to_be_measured()
    {
        var graph = StudioTestServices.CreateGraph();
        graph.Diagram.SetContainer(new(new Blazor.Diagrams.Core.Geometry.Point(0, 0), new Blazor.Diagrams.Core.Geometry.Size(800, 500)));
        var node = graph.AddNode(graph.FindDescription("logger")!);
        graph.Diagram.SelectModel(node, true);
        graph.ZoomToFit();
        graph.Diagram.Zoom.ShouldBe(1);
        graph.Diagram.SuspendRefresh.ShouldBeFalse();
        node.Size = new(210, 196);
        graph.ZoomToFit();
        graph.Diagram.Zoom.ShouldBeGreaterThan(1);
    }

    [Fact]
    public void Palette_clicks_fill_free_positions_and_keep_drop_coordinates_through_undo_redo()
    {
        var graph = StudioTestServices.CreateGraph();
        graph.LoadJson("""{"Workflows":{"main":{}}}""");
        graph.ResetHistory();
        var scheduler = graph.AddNode(graph.FindDescription("scheduler")!);
        var drop = new Blazor.Diagrams.Core.Geometry.Point(scheduler.Position.X + 280, scheduler.Position.Y + 10);
        var source = graph.AddNode(graph.FindDescription("source")!, drop);
        source.Position.ShouldBe(drop);
        var mapper = graph.AddNode(graph.FindDescription("mapper")!);
        mapper.Position.X.ShouldBeGreaterThan(source.Position.X);
        graph.RecordEdit();
        var before = graph.ToProjectJson();
        var logger = graph.AddNode(graph.FindDescription("logger")!);
        logger.Position.X.ShouldBeGreaterThan(mapper.Position.X);
        graph.RecordEdit();
        var after = graph.ToProjectJson();
        graph.Undo();
        graph.ToProjectJson().ShouldBe(before);
        graph.Redo();
        graph.ToProjectJson().ShouldBe(after);
    }

    [Fact]
    public void Removing_a_router_port_preserves_its_link_and_undo_restores_the_visual_connection()
    {
        var graph = StudioTestServices.CreateGraph();
        graph.LoadJson("""{"Workflows":{"main":{"route":{"Type":"router","Engine":"jsonata","Routes":{"Hot":"true"},"Hot":{"Address":"sink.Input","Condition":"true","Engine":"jsonata"}},"sink":{"Type":"logger"}}}}""");
        graph.ResetHistory();
        var before = graph.ToJson();
        graph.Diagram.Links.Count.ShouldBe(1);
        var router = graph.Diagram.Nodes.OfType<FlowNodeModel>().Single(node => node.ComponentType == "router");
        router.Configuration["Routes"] = JsonSerializer.Deserialize<JsonElement>("{}");
        graph.RecordEdit();
        var after = graph.ToJson();
        graph.Diagram.Links.ShouldBeEmpty();
        JsonNode.DeepEquals(JsonNode.Parse(before)!["Workflows"]!["main"]!["route"]!["Hot"],
            JsonNode.Parse(after)!["Workflows"]!["main"]!["route"]!["Hot"]).ShouldBeTrue();
        graph.Undo();
        graph.ToJson().ShouldBe(before);
        graph.Diagram.Links.Count.ShouldBe(1);
        graph.Redo();
        graph.ToJson().ShouldBe(after);
        graph.Diagram.Links.ShouldBeEmpty();
    }

    public static IEnumerable<object[]> Types()
    {
        using var services = new ServiceCollection().AddCatalogComponents(StudioTestServices.InstalledCatalog).BuildServiceProvider();
        return services.GetServices<IComponentDescription>().Select(item => new object[] { item.Palette.ComponentType }).ToArray();
    }

    [Theory]
    [MemberData(nameof(Types))]
    public void Each_component_can_be_created_imported_and_undone_without_rewriting_configuration(string type)
    {
        var graph = StudioTestServices.CreateGraph();
        const string original = """
            {"KeepCase":{"Outer":true},"Resources":{"external":{"Type":"custom"}},"Workflows":{"main":{},"other":{}}}
            """;
        graph.LoadJson(original);
        graph.ResetHistory();
        var description = graph.FindDescription(type)!;
        var node = graph.AddNode(description);
        node.Configuration["Custom"] = JsonSerializer.SerializeToElement(new { MixedCase = new[] { 1, 2 } });
        graph.RecordEdit();
        var added = graph.ToProjectJson();
        graph.Undo();
        JsonNode.DeepEquals(JsonNode.Parse(original), JsonNode.Parse(graph.ToJson())).ShouldBeTrue();
        graph.Redo();
        graph.ToProjectJson().ShouldBe(added);
        var json = graph.ToJson();
        graph.LoadJson(json);
        graph.Diagram.Nodes.Count.ShouldBe(1);
        graph.ToJson().ShouldBe(json);
        var imported = graph.Diagram.Nodes.OfType<FlowNodeModel>().Single();
        imported.Configuration["Custom"].GetProperty("MixedCase").GetArrayLength().ShouldBe(2);
        var presentation = description.GetPresentation(imported.Configuration);
        imported.InputPortNames.ShouldBe(presentation.Inputs.Select(port => port.Name));
        imported.OutputPortNames.ShouldBe(presentation.Outputs.Select(port => port.Name));
        // Error is a filtered Output, not a separate runtime port.
        imported.Ports.OfType<FlowPortModel>().Count(port => port.IsError)
            .ShouldBe(presentation.Outputs.Any(port => port.Name == "Output") ? 2 : 0);
    }

    [Fact]
    public void Router_ports_follow_configuration_and_preserve_links_through_editing_and_history()
    {
        var graph = StudioTestServices.CreateGraph();
        const string original = """
            {"Workflows":{"main":{
              "route":{"Type":"router","Engine":"jsonata","rOuTeS":{"Hot":"Reading > 30"},
                "Hot":{"Address":"other.sink.Input","Condition":"true","Engine":"jsonata"}}
            },"other":{"sink":{"Type":"logger","Category":"untouched"}}}}
            """;
        graph.LoadJson(original);
        graph.ResetHistory();
        graph.Diagram.Nodes.OfType<FlowNodeModel>().Single().OutputPortNames.ShouldContain("Hot");
        graph.ToJson().ShouldBe(new DesignerConfiguration(original).ToJson());
        var node = graph.Diagram.Nodes.OfType<FlowNodeModel>().Single();
        node.Configuration["rOuTeS"] = JsonSerializer.Deserialize<JsonElement>("""{"Hot":"Reading > 30","Cold":"Reading < 10"}""");
        graph.RecordEdit();
        var edited = graph.ToJson();
        graph.Diagram.Nodes.OfType<FlowNodeModel>().Single().OutputPortNames.ShouldContain("Cold");
        graph.Undo();
        graph.Diagram.Nodes.OfType<FlowNodeModel>().Single().OutputPortNames.ShouldNotContain("Cold");
        graph.Redo();
        graph.ToJson().ShouldBe(edited);

        graph.SelectWorkflow("other");
        graph.Diagram.SelectModel(graph.Diagram.Nodes.Single(), true);
        graph.RenameSelectedNode("renamed");
        JsonNode.Parse(graph.ToJson())!["Workflows"]!["main"]!["route"]!["Hot"]!["Address"]!.GetValue<string>()
            .ShouldBe("other.renamed.Input");
        var renamed = graph.ToJson();
        graph.DeleteSelected();
        graph.RecordEdit();
        JsonNode.Parse(graph.ToJson())!["Workflows"]!["main"]!["route"]!["Hot"].ShouldBeNull();
        graph.Undo();
        graph.ToJson().ShouldBe(renamed);
        graph.Undo();
        graph.ToJson().ShouldBe(edited);
    }

    [Fact]
    public void New_router_links_use_configured_ports_and_remain_visible_after_reload()
    {
        var graph = StudioTestServices.CreateGraph();
        graph.LoadJson("""{"Workflows":{"main":{"route":{"Type":"router","Engine":"jsonata","Routes":{"Hot":"true"}},"sink":{"Type":"logger"}}}}""");
        graph.ResetHistory();
        var proposal = graph.PrepareEdits([new ConnectPortsEdit(
            ComponentAddress.Create("main", "route").Port("Hot"),
            ComponentAddress.Create("main", "sink").Port("Input"))]);
        graph.ApplyEdits(proposal).ShouldBeTrue();
        graph.Diagram.Links.Count.ShouldBe(1);
        var saved = graph.ToJson();
        graph.Undo();
        graph.Diagram.Links.ShouldBeEmpty();
        graph.Redo();
        graph.ToJson().ShouldBe(saved);
        graph.Diagram.Links.Count.ShouldBe(1);
    }

    [Fact]
    public void Validation_uses_existing_registrations_without_resolving_dependencies_or_opening_files()
    {
        const string json = """
            {"Workflows":{"main":{
              "filter":{"Type":"changeFilter","Engine":"unavailable","KeySelector":"missing.jsonata"},
              "dedup":{"Type":"deduplicate","Engine":"unavailable","KeySelector":"missing.jsonata","Retention":"00:01:00"},
              "generate":{"Type":"dataGenerator","Expression":"missing.jsonata"},
              "limit":{"Type":"rateLimit","Limiter":"missing"},
              "retry":{"Type":"retry"},
              "reduce":{"Type":"reducer","Engine":"unavailable","InitialState":"missing.json","Reduce":"missing.jsonata","AreEqual":"missing.jsonata"},
              "route":{"Type":"router","Engine":"unavailable","Routes":{"Next":"missing.jsonata"}},
              "http":{"Type":"http.client","Client":"missing"},
              "mqtt":{"Type":"mqtt.subscriber","Connection":"missing","Topics":["sensors/#"]},
              "log":{"Type":"logger","Logger":"missing"},
              "serial":{"Type":"serial.text","Connection":"missing"},
              "ros":{"Type":"ros.publisher","Node":"missing","Topic":"/telemetry"},
              "trigger":{"Type":"http.trigger","Route":"/telemetry"}
            }}}
            """;
        var validators = StudioTestServices.CreateGraph().PluginValidators;
        var messages = DesignerConfigurationValidator.Validate(json, validators: validators);
        messages.ShouldNotContain(message => message.Severity == ValidationSeverity.Error);
        messages.ShouldContain(message => message.Message.Contains("File access belongs to the runner"));
        messages.ShouldNotContain(message => message.Address == ComponentAddress.Create("main", "serial"));
        messages.ShouldContain(message => message.Address == ComponentAddress.Create("main", "ros") &&
            message.Severity == ValidationSeverity.Information && message.Message.Contains("native ROS and std_msgs"));
        messages.ShouldContain(message => message.Address == ComponentAddress.Create("main", "trigger") &&
            message.Severity == ValidationSeverity.Information && message.Message.Contains("ASP.NET Core"));
        var invalid = json.Replace("\"Retention\":\"00:01:00\"", "\"Retention\":\"00:00:00\"");
        DesignerConfigurationValidator.Validate(invalid, validators: validators).ShouldContain(message =>
            message.Address == ComponentAddress.Create("main", "dedup") && message.Severity == ValidationSeverity.Error);
    }
}
