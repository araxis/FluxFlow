using System.Text.Json;
using System.Text.Json.Nodes;
using FluxFlow.Plugins;
using FluxFlow.Plugins.Routing;
using FluxFlow.Studio.UI.Features.Designer;
using FluxFlow.Studio.UI.Features.Designer.Canvas;
using Shouldly;
using Xunit;

namespace FluxFlow.Studio.Tests;

public sealed class RoutingPluginTests
{
    [Theory]
    [InlineData("bad.name")]
    [InlineData("Hot:Room")]
    public void Unaddressable_saved_route_does_not_hide_valid_wires_or_change_the_document(string invalid)
    {
        var config = """
            {"Workflows":{"main":{"split":{"Type":"router","Engine":"jsonata",
              "Routes":{"Hot":"true","INVALID":"false"},
              "Hot":{"Address":"target.Input","Condition":"$ < 100","Engine":"jsonata"},
              "INVALID":"target.Input"},"target":{"Type":"gate"}}}}
            """.Replace("INVALID", invalid);
        var missing = new DesignerGraphState([]);
        missing.LoadJson(config);
        var stored = missing.ToProjectJson();
        var installed = new DesignerGraphState(new RoutingPlugin().Description.Components);
        installed.LoadProject(stored);
        var router = installed.Diagram.Nodes.OfType<FlowNodeModel>().Single(n => n.NodeName == "split");
        router.OutputPortNames.ShouldContain("Hot");
        router.OutputPortNames.ShouldNotContain(invalid);
        installed.Diagram.Links.Count.ShouldBe(1);
        JsonNode.DeepEquals(JsonNode.Parse(installed.ToJson()), JsonNode.Parse(config)).ShouldBeTrue();
    }

    [Fact]
    public void Transported_catalog_restores_named_ports_and_conditional_links_after_missing_plugin_save()
    {
        const string configuration = """
            {"Workflows":{"main":{
              "split":{"Type":"router","Engine":"jsonata","rOuTeS":{"Hot":"$ > 30","Cold":"$ < 10"},
                "Hot":{"Address":"target.Input","Condition":"$ < 100","Engine":"jsonata"},"Extra":{"saved":true}},
              "target":{"Type":"gate","InitiallyOpen":true}}}}
            """;
        var wireCatalog = JsonSerializer.Serialize(new[] { new RoutingPlugin().Description }, PluginJsonContext.Default.PluginDescriptionArray);
        var plugin = JsonSerializer.Deserialize(wireCatalog, PluginJsonContext.Default.PluginDescriptionArray)!.Single();
        var installed = new DesignerGraphState(plugin.Components);
        installed.LoadJson(configuration);
        var saved = installed.ToProjectJson();

        var missing = new DesignerGraphState([]);
        missing.LoadProject(saved);
        missing.FindDescription("router").ShouldBeNull();
        var missingSave = missing.ToProjectJson();
        JsonNode.DeepEquals(JsonNode.Parse(missingSave), JsonNode.Parse(saved)).ShouldBeTrue();

        var reopened = new DesignerGraphState(plugin.Components);
        reopened.LoadProject(missingSave);
        var router = reopened.Diagram.Nodes.OfType<FlowNodeModel>().Single(node => node.NodeName == "split");
        router.Presentation.Outputs.Select(port => port.Name).ShouldBe(new[] { "Output", "Unmatched", "Events", "Hot", "Cold" });
        reopened.Diagram.Links.Count.ShouldBe(1);
        JsonNode.DeepEquals(JsonNode.Parse(reopened.ToJson()), JsonNode.Parse(configuration)).ShouldBeTrue();

        reopened.ResetHistory();
        reopened.Diagram.SelectModel(reopened.Diagram.Links.Single(), true);
        reopened.DeleteSelected();
        reopened.RecordEdit();
        var withoutWire = JsonNode.Parse(configuration)!;
        withoutWire["Workflows"]!["main"]!["split"]!.AsObject().Remove("Hot");
        JsonNode.DeepEquals(JsonNode.Parse(reopened.ToJson()), withoutWire).ShouldBeTrue();

        reopened.Undo();
        JsonNode.DeepEquals(JsonNode.Parse(reopened.ToProjectJson()), JsonNode.Parse(saved)).ShouldBeTrue();
        reopened.Redo();
        JsonNode.DeepEquals(JsonNode.Parse(reopened.ToJson()), withoutWire).ShouldBeTrue();
    }
}
