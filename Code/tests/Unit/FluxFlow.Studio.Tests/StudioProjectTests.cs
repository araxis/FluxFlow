using System.Text.Json;
using System.Text.Json.Nodes;
using Blazor.Diagrams.Core.Anchors;
using Blazor.Diagrams.Core.Models;
using FluxFlow.Studio.UI.Features.Designer;
using FluxFlow.Studio.UI.Features.Designer.Canvas;
using FluxFlow.Studio.UI.Features.Designer.Projects;
using FluxFlow.Studio.UI.Features.Designer.Settings;
using Shouldly;
using Xunit;

namespace FluxFlow.Studio.Tests;

public sealed class StudioProjectTests
{
    private const string Configuration = """
        {"HostOption":{"KeepCase":true},"FluxFlow":{"Resources":{"external":{"Type":"custom","Settings":[1,null,{"MixedCase":"Value"}]}},"Workflows":{
          "main":{"map":{"Type":"mapper","Engine":"jsonata","Expression":"$","Error":"sink.Input","Extra":{"KeepMe":1234567890123456789}},"sink":{"Type":"batch","BatchSize":2}},
          "other":{"map":{"Type":"mapper","Engine":"custom-engine","Expression":"$headers.MixedCase"},"unknown":{"Type":"custom","Value":{"UPPER":true}}}
        }}}
        """;

    [Fact]
    public void Project_round_trip_preserves_configuration_geometry_colors_and_link_sides()
    {
        var graph = Create();
        var source = Node(graph, "map");
        graph.Diagram.SelectModel(source, true);
        source.SetPosition(-25, 300);
        graph.SetSelectedNodeColor("#abcdef");
        graph.SetPortColorOverrides(new(Output: "#123456", Error: "#bb1122"));
        var link = graph.Diagram.Links.Single();
        link.SetSource(new SinglePortAnchor(source.Ports.OfType<FlowPortModel>().Single(p => p.IsError && p.Alignment == PortAlignment.Left)));
        link.SetTarget(new SinglePortAnchor(Node(graph, "sink").Ports.OfType<FlowPortModel>().Single(p => p.IsInput && p.Alignment == PortAlignment.Right)));
        graph.RecordEdit();
        graph.SelectWorkflow("other");
        Node(graph, "map").SetPosition(800, 120);
        graph.Diagram.SelectModel(Node(graph, "map"), true);
        graph.SetSelectedNodeColor("#123456");

        var saved = graph.ToProjectJson();
        var reopened = StudioTestServices.CreateGraph();
        reopened.LoadProject(saved);
        reopened.ResetHistory();
        reopened.CanUndo.ShouldBeFalse();
        reopened.WorkflowName.ShouldBe("other");
        Node(reopened, "map").Position.X.ShouldBe(800);
        Node(reopened, "map").Color.ShouldBe("#123456");
        reopened.PortColorOverrides.ShouldBe(graph.PortColorOverrides);
        reopened.SelectWorkflow("main");
        Node(reopened, "map").Position.X.ShouldBe(-25);
        Node(reopened, "map").Position.Y.ShouldBe(300);
        Node(reopened, "map").Color.ShouldBe("#abcdef");
        var restored = reopened.Diagram.Links.Single();
        ((SinglePortAnchor)restored.Source).Port.Alignment.ShouldBe(PortAlignment.Left);
        ((SinglePortAnchor)restored.Target).Port.Alignment.ShouldBe(PortAlignment.Right);
        restored.TargetMarker.ShouldBe(LinkMarker.Arrow);
        ((FlowPortModel)((SinglePortAnchor)restored.Source).Port).IsError.ShouldBeTrue();
        JsonNode.DeepEquals(JsonNode.Parse(Configuration), JsonNode.Parse(reopened.ToJson())).ShouldBeTrue();
        JsonNode.DeepEquals(JsonNode.Parse(Configuration)!["FluxFlow"], JsonNode.Parse(reopened.ExportConfiguration())).ShouldBeTrue();
        var exported = JsonNode.Parse(reopened.ExportConfiguration())!.AsObject();
        exported.ContainsKey("PortColors").ShouldBeFalse();
        exported.ContainsKey("HostOption").ShouldBeFalse();
        exported.ContainsKey("Nodes").ShouldBeFalse();

        reopened.Diagram.SelectModel(Node(reopened, "map"), true);
        reopened.RenameSelectedNode("renamed");
        reopened.RecordEdit();
        reopened.Undo();
        Node(reopened, "map").Color.ShouldBe("#abcdef");
        reopened.PortColorOverrides.Error.ShouldBe("#bb1122");
        reopened.Redo();
        Node(reopened, "renamed").Position.X.ShouldBe(-25);
    }

    [Fact]
    public void Project_color_edit_is_one_undo_action_and_import_resets_only_project_overrides()
    {
        var graph = Create();
        var original = graph.ToJson();
        var colors = new PortColors(Input: "#112233", Events: "#abcdef");
        graph.SetPortColorOverrides(colors);
        graph.SetPortColorOverrides(colors);
        graph.CanUndo.ShouldBeTrue();
        graph.ToJson().ShouldBe(original);
        graph.Undo();
        graph.PortColorOverrides.ShouldBe(new PortColors());
        graph.CanUndo.ShouldBeFalse();
        graph.Redo();
        graph.PortColorOverrides.ShouldBe(colors);
        graph.LoadJson(Configuration, "FluxFlow");
        graph.PortColorOverrides.ShouldBe(new PortColors());
    }

    [Theory]
    [InlineData("Version")]
    [InlineData("Format")]
    [InlineData("Color")]
    [InlineData("Side")]
    [InlineData("Address")]
    [InlineData("Configuration")]
    [InlineData("UnknownField")]
    public void Invalid_projects_do_not_replace_open_configuration_or_history(string defect)
    {
        var graph = Create();
        graph.SetPortColorOverrides(new(Error: "#abcdef"));
        var before = graph.ToProjectJson();
        var invalid = JsonNode.Parse(before)!;
        switch (defect)
        {
            case "Version": invalid["Version"] = 2; break;
            case "Format": invalid.AsObject().Remove("Format"); break;
            case "Color": invalid["PortColors"]!["Error"] = "red;display:none"; break;
            case "Side": invalid["Links"]!["main"]![0]!["SourceSide"] = "Bottom"; break;
            case "Address": invalid["Links"]!["main"]![0]!["Source"] = "bad address"; break;
            case "Configuration": invalid["Configuration"]!["FluxFlow"]!["Workflows"]!["main"]!["map"]!["Type"] = 123; break;
            case "UnknownField": invalid["UnsupportedMetadata"] = true; break;
        }
        Should.Throw<Exception>(() => graph.LoadProject(invalid.ToJsonString()));
        graph.ToProjectJson().ShouldBe(before);
        graph.CanUndo.ShouldBeTrue();
    }

    [Fact]
    public void Engine_json_is_not_accepted_as_a_studio_project() =>
        Should.Throw<JsonException>(() => StudioProject.Read(Configuration));

    [Fact]
    public void Port_colors_inherit_per_role_and_reset_without_altering_other_roles()
    {
        var defaults = new PortColors("#111111", "#222222", "#333333", "#444444");
        var studio = new PortColors(Events: "#eeeeee").Resolve(defaults);
        var project = new PortColors(Output: "#abcdef");
        project.Resolve(studio).ShouldBe(new PortColors("#111111", "#abcdef", "#eeeeee", "#444444"));
        project.With("Output", null).Resolve(studio).ShouldBe(studio);
        new PortColors().ToStyle(new PortColors()).ShouldBe("");
        project.ToStyle(new PortColors(Events: "#eeeeee")).ShouldBe("--studio-port-output:#abcdef;--studio-port-events:#eeeeee");
    }

    [Fact]
    public async Task Studio_preferences_survive_a_new_session_and_failed_save_preserves_current_values()
    {
        var storage = new SettingsStorage();
        var preferences = new StudioPreferences(storage);
        var notifications = new List<(bool Saving, string? Status)>();
        preferences.Changed += () => notifications.Add((preferences.IsSaving, preferences.SaveStatus));
        preferences.SaveStatus.ShouldBeNull();
        var settings = new StudioSettings { Theme = "Light", PortColors = new(Events: "#abcd12") };
        await preferences.SaveAsync(settings);
        notifications.ShouldBe(new (bool, string?)[] { (true, "Saving…"), (false, "Saved") });
        var reopened = new StudioPreferences(storage);
        await reopened.LoadAsync();
        JsonSerializer.Serialize(reopened.Value, StudioJsonContext.Default.StudioSettings).ShouldBe(JsonSerializer.Serialize(settings, StudioJsonContext.Default.StudioSettings));
        reopened.SaveStatus.ShouldBeNull();
        storage.FailWrites = true;
        await Should.ThrowAsync<IOException>(() => reopened.SaveAsync(settings with { Theme = "Dark" }));
        JsonSerializer.Serialize(reopened.Value, StudioJsonContext.Default.StudioSettings).ShouldBe(JsonSerializer.Serialize(settings, StudioJsonContext.Default.StudioSettings));
        reopened.IsSaving.ShouldBeFalse();
        reopened.SaveStatus.ShouldBe("Settings could not be saved");
        var again = new StudioPreferences(storage);
        await again.LoadAsync();
        JsonSerializer.Serialize(again.Value, StudioJsonContext.Default.StudioSettings).ShouldBe(JsonSerializer.Serialize(settings, StudioJsonContext.Default.StudioSettings));
        storage.FailWrites = false;
        await reopened.SaveAsync(settings with { Theme = "Dark" });
        reopened.SaveStatus.ShouldBe("Saved");
        reopened.Value.Theme.ShouldBe("Dark");
    }

    private static DesignerGraphState Create()
    {
        var graph = StudioTestServices.CreateGraph();
        graph.LoadJson(Configuration, "FluxFlow");
        graph.ResetHistory();
        return graph;
    }

    private static FlowNodeModel Node(DesignerGraphState graph, string name) =>
        graph.Diagram.Nodes.OfType<FlowNodeModel>().Single(node => node.NodeName == name);

    private sealed class SettingsStorage : IStudioSettingsStorage
    {
        private string? _json;
        public bool FailWrites { get; set; }
        public Task<string?> ReadAsync() => Task.FromResult(_json);
        public Task WriteAsync(string json)
        {
            if (FailWrites) throw new IOException("Storage unavailable");
            _json = json;
            return Task.CompletedTask;
        }
    }
}
