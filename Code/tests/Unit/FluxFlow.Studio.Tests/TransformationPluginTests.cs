using System.Text.Json;
using System.Text.Json.Nodes;
using FluxFlow.Plugins;
using FluxFlow.Plugins.Transformation;
using FluxFlow.Studio.UI.Features.Designer;
using Shouldly;
using Xunit;

namespace FluxFlow.Studio.Tests;

public sealed class TransformationPluginTests
{
    [Fact]
    public void Transported_mapper_preset_applies_only_to_new_nodes_and_preserves_imported_engines()
    {
        var serialized = JsonSerializer.Serialize(new[] { new TransformationPlugin().Description }, PluginJsonContext.Default.PluginDescriptionArray);
        var plugin = JsonSerializer.Deserialize(serialized, PluginJsonContext.Default.PluginDescriptionArray)!.Single();
        var mapper = plugin.Components.Single(c => c.Palette.ComponentType == "mapper");
        var fresh = new Dictionary<string, JsonElement>();
        mapper.InitializeConfiguration(fresh);
        fresh["Engine"].GetString().ShouldBe("jsonata");
        mapper.GetPresentation(fresh).DisplayName.ShouldBe("JSONata Mapper");
        mapper.GetInspector(fresh).Sections.SelectMany(s => s.Options).ShouldNotContain(o => o.Name == "Engine");

        const string original = """
            {"Workflows":{"main":{"custom":{"Type":"mapper","eNgInE":"csharp","Expression":"file:custom.cs","Future":{"keep":true},"Output":"batch.Input"},
            "batch":{"Type":"batch","BatchSize":3,"MaxWait":"00:00:02"}}}}
            """;
        var graph = new DesignerGraphState(plugin.Components);
        graph.LoadJson(original);
        var saved = graph.ToProjectJson();
        var missing = new DesignerGraphState([]);
        missing.LoadProject(saved);
        missing.FindDescription("mapper").ShouldBeNull();
        graph.LoadProject(missing.ToProjectJson());
        JsonNode.DeepEquals(JsonNode.Parse(original), JsonNode.Parse(graph.ToJson())).ShouldBeTrue();
        var settings = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            JsonNode.Parse(graph.ToJson())!["Workflows"]!["main"]!["custom"]!.ToJsonString())!;
        mapper.GetPresentation(settings).DisplayName.ShouldBe("Mapper");
        mapper.GetInspector(settings).Sections.SelectMany(s => s.Options).ShouldContain(o => o.Name == "Engine");
    }
}
