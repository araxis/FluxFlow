using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Blazor.Diagrams.Core.Anchors;
using FluxFlow.Application;
using FluxFlow.Application.Addressing;
using FluxFlow.Configuration.Application;
using FluxFlow.Configuration.Components.Batching;
using FluxFlow.Configuration.Components.Mapping;
using FluxFlow.Configuration.Components.Routing;
using FluxFlow.Configuration.Components.Scheduling;
using FluxFlow.Configuration.Components.Sources;
using FluxFlow.Studio.UI.Features.Designer;
using FluxFlow.Studio.UI.Features.Designer.Canvas;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Studio.Tests;

public sealed class DesignerConfigurationTests
{
    [Theory]
    [InlineData("delete")]
    [InlineData("native")]
    [InlineData("clear")]
    public void Undo_deletion_after_workflow_navigation_returns_to_the_edited_workflow(string action)
    {
        const string json = """
        {"Workflows":{
          "first":{"map":{"Type":"mapper","Output":"second.map.Input"}},
          "second":{"map":{"Type":"mapper"}}
        }}
        """;
        var graph = StudioTestServices.CreateGraph();
        graph.LoadJson(json);
        graph.ResetHistory();
        graph.SelectWorkflow("second");
        var node = graph.Diagram.Nodes.Single();
        graph.Diagram.SelectModel(node, true);
        if (action == "clear") graph.Clear();
        else if (action == "native") graph.Diagram.Nodes.Remove(node);
        else graph.DeleteSelected();
        graph.RecordEdit();
        var deleted = graph.ToJson();
        JsonNode.Parse(deleted)!["Workflows"]!["first"]!["map"]!.AsObject().ContainsKey("Output").ShouldBeFalse();

        graph.Undo();

        graph.WorkflowName.ShouldBe("second");
        graph.NodeCount.ShouldBe(1);
        graph.SelectedNode!.NodeName.ShouldBe("map");
        graph.CanUndo.ShouldBeFalse();
        JsonNode.DeepEquals(JsonNode.Parse(json), JsonNode.Parse(graph.ToJson())).ShouldBeTrue();
        graph.Redo();
        graph.WorkflowName.ShouldBe("second");
        graph.NodeCount.ShouldBe(0);
        graph.CanRedo.ShouldBeFalse();
        JsonNode.DeepEquals(JsonNode.Parse(deleted), JsonNode.Parse(graph.ToJson())).ShouldBeTrue();
        graph.RecordEdit();
        graph.Undo();
        graph.CanUndo.ShouldBeFalse();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Deleting_components_cleans_cross_workflow_links_in_one_history_entry(bool nativeRemoval, bool multiple)
    {
        var original = JsonNode.Parse(Sample)!;
        var workflows = original["FluxFlow"]!["Workflows"]!;
        workflows["main"]!["clock"]!["Output"]!.AsArray().Add("sink.Input");
        workflows["main"]!["map"]!["Input"] = "map.Output";
        workflows["main"]!["sink"]!["Input"] = "map.Output";
        workflows["main"]!["sink"]!["Events"] = "map.Input";
        workflows["main"]!["sink"]!["Extra"] = new JsonObject { ["map.Input"] = "main.map.Input" };
        workflows["main"]!["custom"]!["Output"] = "main.map.Input";
        workflows["other"]!["map"] = new JsonObject { ["Type"] = "mapper", ["Expression"] = "'main.map.Input'" };
        workflows["other"]!["sink"]!["Input"] = new JsonArray("main.map.Output", "main.clock.Output", "System.Events.Output");
        workflows["other"]!["sink"]!["Events"] = new JsonObject { ["address"] = "main.map.Input" };
        workflows["other"]!["sink"]!["Output"] = new JsonArray(
            new JsonObject { ["address"] = "main.map.Input", ["condition"] = "true", ["engine"] = "jsonata" },
            new JsonObject { ["address"] = "map.Input", ["condition"] = "'main.map.Input'", ["engine"] = "custom" },
            "main.map.Input");
        workflows["other"]!["sink"]!["Error"] = new JsonArray("main.map.Input", "main.map.Input");
        var graph = StudioTestServices.CreateGraph();
        graph.LoadJson(original.ToJsonString(), "FluxFlow");
        graph.SelectWorkflow("other");
        graph.SelectWorkflow("main");
        var node = graph.Diagram.Nodes.OfType<FlowNodeModel>().Single(item => item.NodeName == "map");
        node.SetPosition(456, 234);
        graph.Diagram.SelectModel(node, true);
        graph.SetSelectedNodeColor("#8247b5");
        var error = graph.Diagram.Links.OfType<FlowLinkModel>().Single(item => item.Original.IsError);
        error.SetSource(new SinglePortAnchor(Handle(node, "Error", true)));
        graph.ResetHistory();
        if (multiple)
            graph.Diagram.SelectModel(graph.Diagram.Nodes.OfType<FlowNodeModel>().Single(item => item.NodeName == "clock"), false);

        if (nativeRemoval)
            foreach (var selected in graph.Diagram.GetSelectedModels().OfType<FlowNodeModel>().ToArray()) graph.Diagram.Nodes.Remove(selected);
        else graph.DeleteSelected();
        graph.RecordEdit();

        var expected = original.DeepClone();
        var expectedWorkflows = expected["FluxFlow"]!["Workflows"]!;
        var main = expectedWorkflows["main"]!.AsObject();
        main.Remove("map");
        main["sink"]!.AsObject().Remove("Input");
        main["sink"]!.AsObject().Remove("Events");
        if (multiple) main.Remove("clock");
        else main["clock"]!["Output"] = new JsonArray("sink.Input");
        var remote = expectedWorkflows["other"]!["sink"]!.AsObject();
        remote["Input"]!.AsArray().RemoveAt(0);
        if (multiple) remote["Input"]!.AsArray().RemoveAt(0);
        remote.Remove("Events");
        remote.Remove("Error");
        remote["Output"]!.AsArray().RemoveAt(2);
        remote["Output"]!.AsArray().RemoveAt(0);
        JsonNode.DeepEquals(expected, JsonNode.Parse(graph.ToJson())).ShouldBeTrue();
        graph.RecordEdit(); // A save must not recreate links or add another undo step.
        graph.Undo();
        graph.CanUndo.ShouldBeFalse();
        JsonNode.DeepEquals(original, JsonNode.Parse(graph.ToJson())).ShouldBeTrue();
        var restored = graph.Diagram.Nodes.OfType<FlowNodeModel>().Single(item => item.NodeName == "map");
        restored.Position.X.ShouldBe(456);
        restored.Position.Y.ShouldBe(234);
        restored.Color.ShouldBe("#8247b5");
        ((SinglePortAnchor)graph.Diagram.Links.OfType<FlowLinkModel>().Single(item => item.Original.IsError).Source)
            .Port.Alignment.ShouldBe(Blazor.Diagrams.Core.Models.PortAlignment.Left);
        graph.Redo();
        graph.CanRedo.ShouldBeFalse();
        graph.SelectWorkflow("other");
        JsonNode.DeepEquals(expected, JsonNode.Parse(graph.ToJson())).ShouldBeTrue();
        graph.SelectWorkflow("main");
        graph.RecordEdit();
        graph.Undo();
        graph.CanUndo.ShouldBeFalse();
        JsonNode.DeepEquals(original, JsonNode.Parse(graph.ToJson())).ShouldBeTrue();
    }

    [Fact]
    public void Clear_cleans_references_from_other_workflows_and_undo_restores_them()
    {
        var original = JsonNode.Parse(Sample)!;
        original["FluxFlow"]!["Workflows"]!["other"]!["sink"]!["Output"] = "main.map.Input";
        var graph = StudioTestServices.CreateGraph();
        graph.LoadJson(original.ToJsonString(), "FluxFlow");
        graph.ResetHistory();
        graph.Clear();
        graph.RecordEdit();
        var expected = original.DeepClone();
        var main = expected["FluxFlow"]!["Workflows"]!["main"]!.AsObject();
        main.Remove("clock");
        main.Remove("map");
        main.Remove("sink");
        expected["FluxFlow"]!["Workflows"]!["other"]!["sink"]!.AsObject().Remove("Output");
        JsonNode.DeepEquals(expected, JsonNode.Parse(graph.ToJson())).ShouldBeTrue();
        graph.Undo();
        graph.CanUndo.ShouldBeFalse();
        JsonNode.DeepEquals(original, JsonNode.Parse(graph.ToJson())).ShouldBeTrue();
    }

    [Fact]
    public void Rename_uses_full_addresses_when_the_name_cannot_be_a_relative_reference()
    {
        const string json = """
        {"Workflows":{"main":{
          "map":{"Type":"mapper","Output":"map.Input"},
          "sink":{"Type":"batch","Input":{"address":"map.Output","condition":"true","engine":"jsonata"}}
        }}}
        """;
        var graph = StudioTestServices.CreateGraph();
        graph.LoadJson(json);
        graph.ResetHistory();
        graph.Diagram.SelectModel(graph.Diagram.Nodes.OfType<FlowNodeModel>().Single(node => node.NodeName == "map"), true);

        graph.RenameSelectedNode("System").ShouldBeTrue();

        var renamed = JsonNode.Parse(graph.ToJson())!;
        renamed["Workflows"]!["main"]!["System"]!["Output"]!.GetValue<string>().ShouldBe("main.System.Input");
        renamed["Workflows"]!["main"]!["sink"]!["Input"]!["address"]!.GetValue<string>().ShouldBe("main.System.Output");
        graph.SelectedNode!.NodeName.ShouldBe("System");
        graph.Undo();
        JsonNode.DeepEquals(JsonNode.Parse(json), JsonNode.Parse(graph.ToJson())).ShouldBeTrue();
        graph.CanUndo.ShouldBeFalse();
        graph.Redo();
        JsonNode.DeepEquals(renamed, JsonNode.Parse(graph.ToJson())).ShouldBeTrue();
    }

    [Fact]
    public void Undo_rename_after_workflow_navigation_restores_the_renamed_component_selection()
    {
        var graph = StudioTestServices.CreateGraph();
        graph.LoadJson("""{"Workflows":{"first":{"map":{"Type":"mapper"}},"second":{"map":{"Type":"mapper"}}}}""");
        graph.ResetHistory();
        graph.SelectWorkflow("second");
        graph.Diagram.SelectModel(graph.Diagram.Nodes.Single(), true);
        graph.RenameSelectedNode("renamed");

        graph.Undo();

        graph.WorkflowName.ShouldBe("second");
        graph.SelectedNode!.NodeName.ShouldBe("map");
        graph.CanUndo.ShouldBeFalse();
        graph.Redo();
        graph.WorkflowName.ShouldBe("second");
        graph.SelectedNode!.NodeName.ShouldBe("renamed");
        graph.RenameSelectedNode("final");
        graph.Undo();
        graph.SelectedNode!.NodeName.ShouldBe("renamed");
        graph.Undo();
        graph.SelectedNode!.NodeName.ShouldBe("map");
    }

    [Theory]
    [InlineData("renamed")]
    [InlineData("Map")]
    public void Rename_updates_only_link_addresses_and_is_one_undoable_edit(string name)
    {
        var original = JsonNode.Parse(Sample)!;
        var workflows = original["FluxFlow"]!["Workflows"]!;
        workflows["main"]!["map"]!["Input"] = "map.Output";
        workflows["main"]!["map"]!["Output"] = "main.map.Input";
        workflows["main"]!["map"]!["Expression"] = "'map.Input'";
        workflows["main"]!["map"]!["Headers"] = new JsonObject { ["map.Input"] = "main.map.Input" };
        workflows["main"]!["sink"]!["Error"] = "map.Input";
        workflows["other"]!["sink"]!["Input"] = "main.map.Output";
        workflows["other"]!["sink"]!["Events"] = new JsonObject { ["address"] = "main.map.Input", ["condition"] = "'map.Input'", ["engine"] = "custom" };
        var graph = StudioTestServices.CreateGraph();
        graph.LoadJson(original.ToJsonString(), "FluxFlow");
        graph.SelectWorkflow("other");
        graph.SelectWorkflow("main");
        var node = graph.Diagram.Nodes.OfType<FlowNodeModel>().Single(item => item.NodeName == "map");
        node.SetPosition(456, 234);
        graph.Diagram.SelectModel(node, true);
        graph.SetSelectedNodeColor("#8247b5");
        var link = graph.Diagram.Links.OfType<FlowLinkModel>().Single(item => item.Original.Property == "Error" && item.Original.Owner.ComponentName == "map");
        link.SetSource(new SinglePortAnchor(Handle(node, "Error", true)));
        graph.ResetHistory();

        graph.RenameSelectedNode(name).ShouldBeTrue();

        var expected = original.DeepClone();
        var expectedWorkflows = expected["FluxFlow"]!["Workflows"]!;
        var main = expectedWorkflows["main"]!.AsObject();
        var renamed = main["map"]!.DeepClone();
        main.Remove("map");
        main[name] = renamed;
        renamed["Input"] = $"{name}.Output";
        renamed["Output"] = $"main.{name}.Input";
        main["clock"]!["Output"]![0] = $"{name}.Input";
        main["clock"]!["Output"]![1]!["address"] = $"{name}.Input";
        main["sink"]!["Error"] = $"{name}.Input";
        expectedWorkflows["other"]!["sink"]!["Input"] = $"main.{name}.Output";
        expectedWorkflows["other"]!["sink"]!["Events"]!["address"] = $"main.{name}.Input";
        AssertRenamed();
        JsonNode.Parse(graph.ToJson())!["FluxFlow"]!["Workflows"]!["main"]!.AsObject().Select(pair => pair.Key)
            .ShouldBe(new[] { "Resources", "clock", name, "sink", "custom" });
        graph.Undo();
        graph.CanUndo.ShouldBeFalse();
        graph.SelectedNode!.NodeName.ShouldBe("map");
        graph.SelectedNode.Color.ShouldBe("#8247b5");
        graph.SelectedNode.Position.X.ShouldBe(456);
        JsonNode.DeepEquals(original, JsonNode.Parse(graph.ToJson())).ShouldBeTrue();
        graph.Redo();
        AssertRenamed();
        graph.CanRedo.ShouldBeFalse();
        graph.SelectWorkflow("other");
        graph.SelectWorkflow("main");
        graph.Diagram.SelectModel(graph.Diagram.Nodes.OfType<FlowNodeModel>().Single(item => item.NodeName == name), true);
        AssertRenamed();
        graph.RecordEdit();
        graph.Undo();
        graph.CanUndo.ShouldBeFalse();

        void AssertRenamed()
        {
            JsonNode.DeepEquals(expected, JsonNode.Parse(graph.ToJson())).ShouldBeTrue();
            graph.SelectedNode!.NodeName.ShouldBe(name);
            graph.SelectedNode.Color.ShouldBe("#8247b5");
            graph.SelectedNode.Position.X.ShouldBe(456);
            graph.SelectedNode.Position.Y.ShouldBe(234);
            var error = graph.Diagram.Links.OfType<FlowLinkModel>().Single(item => item.Original.IsError && item.Original.Owner.ComponentName == name);
            ((SinglePortAnchor)error.Source).Port.Alignment.ShouldBe(Blazor.Diagrams.Core.Models.PortAlignment.Left);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("invalid.name")]
    [InlineData("invalid:name")]
    [InlineData("sink")]
    [InlineData("SINK")]
    [InlineData("CUSTOM")]
    [InlineData("Resources")]
    public void Invalid_rename_preserves_configuration_selection_and_history(string name)
    {
        var graph = StudioTestServices.CreateGraph();
        graph.LoadJson(Sample, "FluxFlow");
        graph.ResetHistory();
        var node = graph.Diagram.Nodes.OfType<FlowNodeModel>().Single(item => item.NodeName == "map");
        graph.Diagram.SelectModel(node, true);
        Should.Throw<ArgumentException>(() => graph.RenameSelectedNode(name));
        graph.SelectedNode.ShouldBeSameAs(node);
        graph.CanUndo.ShouldBeFalse();
        JsonNode.DeepEquals(JsonNode.Parse(Sample), JsonNode.Parse(graph.ToJson())).ShouldBeTrue();
        graph.RenameSelectedNode("map").ShouldBeFalse();
        graph.CanUndo.ShouldBeFalse();
    }

    [Fact]
    public void Rename_reserves_resources_even_without_resources_and_clears_redo_on_a_new_edit()
    {
        var graph = StudioTestServices.CreateGraph();
        graph.LoadJson("""{"Workflows":{"main":{"map":{"Type":"mapper"}}}}""");
        graph.ResetHistory();
        graph.Diagram.SelectModel(graph.Diagram.Nodes.Single(), true);
        Should.Throw<ArgumentException>(() => graph.RenameSelectedNode("rEsOuRcEs"));
        graph.RenameSelectedNode("first");
        graph.Undo();
        graph.RenameSelectedNode("second");
        graph.CanRedo.ShouldBeFalse();
        graph.Undo();
        graph.CanUndo.ShouldBeFalse();
        graph.SelectedNode!.NodeName.ShouldBe("map");
    }

    [Fact]
    public void Palette_mapper_presets_jsonata_and_retains_it_through_undo_redo()
    {
        var graph = StudioTestServices.CreateGraph();
        graph.ResetHistory();
        var item = graph.FindDescription("mapper")!;
        item.Palette.DisplayName.ShouldBe("JSONata Mapper");
        var node = graph.AddNode(item);
        node.Configuration["Engine"].GetString().ShouldBe("jsonata");
        node.Description.GetInspector(node.Configuration).Sections.SelectMany(section => section.Options)
            .ShouldNotContain(option => option.Name == "Engine");
        graph.RecordEdit();
        var saved = graph.ToJson();
        JsonNode.Parse(saved)!["Workflows"]!["main"]![node.NodeName]!["Type"]!.GetValue<string>().ShouldBe("mapper");

        graph.Undo();
        graph.NodeCount.ShouldBe(0);
        graph.Redo();
        var restored = graph.Diagram.Nodes.OfType<FlowNodeModel>().Single();
        restored.Title.ShouldBe("JSONata Mapper");
        restored.Configuration["Engine"].GetString().ShouldBe("jsonata");
        JsonNode.DeepEquals(JsonNode.Parse(saved), JsonNode.Parse(graph.ToJson())).ShouldBeTrue();
    }

    [Theory]
    [InlineData("jsonata", "JSONata Mapper", false)]
    [InlineData("JSONATA", "Mapper", true)]
    [InlineData(" jsonata ", "Mapper", true)]
    [InlineData("csharp", "Mapper", true)]
    [InlineData("CustomEngine", "Mapper", true)]
    [InlineData(null, "Mapper", true)]
    public void Imported_mapper_engine_and_property_spelling_are_preserved(string? engine, string title, bool editableEngine)
    {
        var values = new JsonObject { ["Type"] = "mapper", ["eXpReSsIoN"] = "$", ["Extra"] = 42 };
        if (engine is not null) values["eNgInE"] = engine;
        var document = new JsonObject { ["Workflows"] = new JsonObject { ["main"] = new JsonObject { ["map"] = values } } };
        var graph = StudioTestServices.CreateGraph();
        graph.LoadJson(document.ToJsonString());
        graph.ResetHistory();
        var node = graph.Diagram.Nodes.OfType<FlowNodeModel>().Single();
        node.Title.ShouldBe(title);
        node.Description.GetInspector(node.Configuration).Sections.SelectMany(section => section.Options)
            .Any(option => option.Name == "Engine").ShouldBe(editableEngine);
        JsonNode.DeepEquals(document, JsonNode.Parse(graph.ToJson())).ShouldBeTrue();

        node.Configuration["eXpReSsIoN"] = JsonSerializer.SerializeToElement("$input");
        graph.RecordEdit();
        var edited = document.DeepClone();
        edited["Workflows"]!["main"]!["map"]!["eXpReSsIoN"] = "$input";
        JsonNode.DeepEquals(edited, JsonNode.Parse(graph.ToJson())).ShouldBeTrue();

        graph.Undo();
        JsonNode.DeepEquals(document, JsonNode.Parse(graph.ToJson())).ShouldBeTrue();
        graph.Diagram.Nodes.OfType<FlowNodeModel>().Single().Title.ShouldBe(title);
        graph.Redo();
        JsonNode.DeepEquals(edited, JsonNode.Parse(graph.ToJson())).ShouldBeTrue();
        var restored = graph.Diagram.Nodes.OfType<FlowNodeModel>().Single();
        restored.Title.ShouldBe(title);
        restored.Description.GetInspector(restored.Configuration).Sections.SelectMany(section => section.Options)
            .Any(option => option.Name == "Engine").ShouldBe(editableEngine);
    }

    [Fact]
    public void Removing_an_earlier_array_entry_does_not_select_or_edit_its_neighbour()
    {
        var graph = StudioTestServices.CreateGraph();
        var expected = JsonNode.Parse(Sample)!;
        var declarations = expected["FluxFlow"]!["Workflows"]!["main"]!["clock"]!["Output"]!.AsArray();
        declarations.Add(new JsonObject { ["Address"] = "map.Input", ["Condition"] = "third" });
        graph.LoadJson(expected.ToJsonString(), "FluxFlow");
        graph.ResetHistory();
        var links = graph.Diagram.Links.OfType<FlowLinkModel>()
            .Where(link => link.Original.Owner.ComponentName == "clock" && link.Original.Property == "Output").ToArray();
        graph.Diagram.SelectModel(links[1], true);
        graph.Diagram.Links.Remove(links[0]);
        graph.RecordEdit();

        graph.SelectedLink.ShouldBeNull();
        Should.Throw<InvalidOperationException>(() => graph.UpdateSelectedLink("wrong link", null));
        declarations.RemoveAt(0);
        JsonNode.DeepEquals(expected, JsonNode.Parse(graph.ToJson())).ShouldBeTrue();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Structural_undo_redo_does_not_reselect_a_different_duplicate_endpoint(bool redo, bool replace)
    {
        var graph = StudioTestServices.CreateGraph();
        var original = JsonNode.Parse(Sample)!;
        original["FluxFlow"]!["Workflows"]!["main"]!["clock"]!["Output"]!.AsArray()
            .Add(new JsonObject { ["Address"] = "map.Input", ["Condition"] = "third" });
        graph.LoadJson(original.ToJsonString(), "FluxFlow");
        graph.ResetHistory();
        var first = graph.Diagram.Links.OfType<FlowLinkModel>().First();
        graph.Diagram.Links.Remove(first);
        if (replace) graph.Diagram.Links.Add(new Blazor.Diagrams.Core.Models.LinkModel(
            ((SinglePortAnchor)first.Source).Port, ((SinglePortAnchor)first.Target).Port));
        graph.RecordEdit();
        var afterRemoval = graph.ToJson();
        if (redo) graph.Undo();
        var conditional = graph.Diagram.Links.OfType<FlowLinkModel>().Single(link => link.Original.Condition == "Value > 0");
        graph.Diagram.SelectModel(conditional, true);

        if (redo) graph.Redo();
        else graph.Undo();

        graph.SelectedLink.ShouldBeNull();
        JsonNode.DeepEquals(redo ? JsonNode.Parse(afterRemoval) : original, JsonNode.Parse(graph.ToJson())).ShouldBeTrue();
    }

    [Theory]
    [InlineData("clock", "Output")]
    [InlineData("map", "Error")]
    [InlineData("sink", "Input")]
    public void Selected_link_edits_preserve_declarations_and_support_undo_redo(string owner, string property)
    {
        var graph = StudioTestServices.CreateGraph();
        graph.LoadJson(Sample, "FluxFlow");
        graph.ResetHistory();
        var link = graph.Diagram.Links.OfType<FlowLinkModel>().First(item => item.Original.Owner.ComponentName == owner &&
            item.Original.Property == property);
        graph.Diagram.SelectModel(link, true);

        graph.UpdateSelectedLink(" Value > 5 ", "CustomEngine").ShouldBeTrue();
        graph.RecordEdit();

        var expected = JsonNode.Parse(Sample)!;
        var declaration = expected["FluxFlow"]!["Workflows"]!["main"]![owner]![property]!;
        if (declaration is JsonArray array) declaration = array[0]!;
        var replacement = new JsonObject { ["Address"] = declaration.GetValue<string>(),
            ["Condition"] = " Value > 5 ", ["Engine"] = "CustomEngine" };
        if (declaration.Parent is JsonArray parent) parent[0] = replacement;
        else ((JsonObject)declaration.Parent!)[property] = replacement;
        JsonNode.DeepEquals(expected, JsonNode.Parse(graph.ToJson())).ShouldBeTrue();
        JsonNode.DeepEquals(expected, JsonNode.Parse(graph.ToJson())).ShouldBeTrue();
        graph.SelectedLink!.Original.Condition.ShouldBe(" Value > 5 ");
        graph.SelectedLink.Original.IsError.ShouldBe(property == "Error");
        graph.SelectedLink.Original.IsInput.ShouldBe(property == "Input");

        graph.Undo();
        JsonNode.DeepEquals(JsonNode.Parse(Sample), JsonNode.Parse(graph.ToJson())).ShouldBeTrue();
        graph.SelectedLink!.Original.Condition.ShouldBeNull();
        graph.CanUndo.ShouldBeFalse();
        graph.Redo();
        JsonNode.DeepEquals(expected, JsonNode.Parse(graph.ToJson())).ShouldBeTrue();
        graph.SelectedLink!.Original.Engine.ShouldBe("CustomEngine");
    }

    [Fact]
    public void Editing_duplicate_endpoints_changes_only_selected_array_entry_and_retains_field_casing()
    {
        var graph = StudioTestServices.CreateGraph();
        graph.LoadJson(Sample, "FluxFlow");
        graph.ResetHistory();
        var link = graph.Diagram.Links.OfType<FlowLinkModel>().Single(item => item.Original.Condition is not null);
        graph.Diagram.SelectModel(link, true);
        graph.UpdateSelectedLink("new expression", "another-engine").ShouldBeTrue();
        graph.RecordEdit();

        var expected = JsonNode.Parse(Sample)!;
        var declaration = expected["FluxFlow"]!["Workflows"]!["main"]!["clock"]!["Output"]![1]!.AsObject();
        declaration["condition"] = "new expression";
        declaration["engine"] = "another-engine";
        JsonNode.DeepEquals(expected, JsonNode.Parse(graph.ToJson())).ShouldBeTrue();
        graph.SelectedLink!.Original.Condition.ShouldBe("new expression");
        graph.UpdateSelectedLink("", "").ShouldBeTrue();
        graph.RecordEdit();
        declaration.Remove("condition");
        declaration.Remove("engine");
        JsonNode.DeepEquals(expected, JsonNode.Parse(graph.ToJson())).ShouldBeTrue();
        graph.Undo();
        graph.SelectedLink!.Original.Condition.ShouldBe("new expression");
        graph.Undo();
        JsonNode.DeepEquals(JsonNode.Parse(Sample), JsonNode.Parse(graph.ToJson())).ShouldBeTrue();
    }

    [Theory]
    [InlineData(null, "custom", "requires a 'Condition'")]
    [InlineData(" ", null, "'Condition' cannot be empty")]
    [InlineData("test", " ", "'Engine' must be a non-empty string")]
    public void Invalid_link_edits_preserve_document_selection_and_history(string? condition, string? engine, string diagnostic)
    {
        var graph = StudioTestServices.CreateGraph();
        graph.LoadJson(Sample, "FluxFlow");
        graph.ResetHistory();
        var link = graph.Diagram.Links.OfType<FlowLinkModel>().First();
        graph.Diagram.SelectModel(link, true);
        var error = Should.Throw<JsonException>(() => graph.UpdateSelectedLink(condition, engine));
        error.Message.ShouldContain(diagnostic);
        error.Message.ShouldContain("FluxFlow:Workflows:main:clock:Output:0");
        graph.SelectedLink.ShouldBeSameAs(link);
        graph.RecordEdit();
        graph.CanUndo.ShouldBeFalse();
        JsonNode.DeepEquals(JsonNode.Parse(Sample), JsonNode.Parse(graph.ToJson())).ShouldBeTrue();
    }

    [Fact]
    public void Unchanged_link_edits_keep_original_forms_and_do_not_create_history()
    {
        var graph = StudioTestServices.CreateGraph();
        graph.LoadJson(Sample, "FluxFlow");
        graph.ResetHistory();
        foreach (var link in graph.Diagram.Links.OfType<FlowLinkModel>().ToArray())
        {
            graph.Diagram.SelectModel(link, true);
            graph.UpdateSelectedLink(link.Original.Condition ?? "", link.Original.Engine ?? "").ShouldBeFalse();
            graph.RecordEdit();
            graph.SelectedLink.ShouldBeSameAs(link);
        }
        graph.CanUndo.ShouldBeFalse();
        JsonNode.DeepEquals(JsonNode.Parse(Sample), JsonNode.Parse(graph.ToJson())).ShouldBeTrue();
    }

    [Fact]
    public void Selection_and_geometry_checkpoints_keep_existing_canvas_models()
    {
        var graph = StudioTestServices.CreateGraph();
        graph.LoadJson(Sample, "FluxFlow");
        graph.ResetHistory();
        var nodes = graph.Diagram.Nodes.OfType<FlowNodeModel>().Take(2).ToArray();
        var link = graph.Diagram.Links.First();
        graph.Diagram.SelectModel(nodes[0], false);
        graph.Diagram.SelectModel(nodes[1], false);
        graph.Diagram.SelectModel(link, false);

        graph.RecordEdit();

        graph.CanUndo.ShouldBeFalse();
        graph.Diagram.GetSelectedModels().ShouldContain(nodes[0]);
        graph.Diagram.GetSelectedModels().ShouldContain(nodes[1]);
        graph.Diagram.GetSelectedModels().ShouldContain(link);
        nodes[0].SetPosition(456, 123);
        graph.RecordEdit();
        graph.Diagram.Nodes.ShouldContain(nodes[0]);
        graph.Diagram.GetSelectedModels().ShouldContain(link);
        graph.CanUndo.ShouldBeTrue();
        JsonNode.DeepEquals(JsonNode.Parse(Sample), JsonNode.Parse(graph.ToJson())).ShouldBeTrue();
        graph.Undo();
        graph.Diagram.Nodes.OfType<FlowNodeModel>().Single(node => node.NodeName == nodes[0].NodeName)
            .Position.X.ShouldNotBe(456);
    }

    [Fact]
    public void Replacing_a_link_with_the_same_declaration_does_not_duplicate_it_on_later_saves()
    {
        const string json = """{"Workflows":{"main":{"a":{"Type":"source","Output":"main.b.Input"},"b":{"Type":"mapper"}}}}""";
        var graph = StudioTestServices.CreateGraph();
        graph.LoadJson(json);
        var link = graph.Diagram.Links.Single();
        var source = ((SinglePortAnchor)link.Source).Port;
        var target = ((SinglePortAnchor)link.Target).Port;
        graph.Diagram.Links.Remove(link);
        graph.Diagram.Links.Add(new Blazor.Diagrams.Core.Models.LinkModel(source, target));
        graph.RecordEdit();

        JsonNode.DeepEquals(JsonNode.Parse(json), JsonNode.Parse(graph.ToJson())).ShouldBeTrue();
        JsonNode.DeepEquals(JsonNode.Parse(json), JsonNode.Parse(graph.ToJson())).ShouldBeTrue();
        graph.Diagram.Links.Count.ShouldBe(1);
    }

    [Fact]
    public void Undo_and_redo_preserve_configuration_and_positions_across_workflows()
    {
        var graph = StudioTestServices.CreateGraph();
        graph.LoadJson(Sample, "FluxFlow");
        graph.ResetHistory();
        var clock = graph.Diagram.Nodes.OfType<FlowNodeModel>().Single(node => node.NodeName == "clock");
        clock.SetPosition(321, 123);
        clock.Configuration["interval"] = JsonSerializer.SerializeToElement("00:00:02");
        graph.RecordEdit();
        var edited = graph.ToJson();
        graph.SelectWorkflow("other");
        graph.SelectWorkflow("main");
        clock = graph.Diagram.Nodes.OfType<FlowNodeModel>().Single(node => node.NodeName == "clock");
        clock.Position.X.ShouldBe(321);
        clock.Position.Y.ShouldBe(123);
        graph.RecordEdit(); // Navigation and selection are not edits.
        graph.Undo();
        graph.CanUndo.ShouldBeFalse();
        JsonNode.DeepEquals(JsonNode.Parse(Sample), JsonNode.Parse(graph.ToJson())).ShouldBeTrue();
        graph.Redo();
        JsonNode.DeepEquals(JsonNode.Parse(edited), JsonNode.Parse(graph.ToJson())).ShouldBeTrue();
        graph.Diagram.Nodes.OfType<FlowNodeModel>().Single(node => node.NodeName == "clock").Position.X.ShouldBe(321);
    }

    [Fact]
    public void Adding_workflows_is_undoable_and_new_edits_replace_redo_history()
    {
        var graph = StudioTestServices.CreateGraph();
        graph.LoadJson(Sample, "FluxFlow");
        graph.ResetHistory();
        graph.AddWorkflow("commands");
        graph.RecordEdit();
        graph.WorkflowName.ShouldBe("commands");
        graph.Undo();
        JsonNode.DeepEquals(JsonNode.Parse(Sample), JsonNode.Parse(graph.ToJson())).ShouldBeTrue();
        graph.CanRedo.ShouldBeTrue();
        graph.AddWorkflow("diagnostics");
        graph.RecordEdit();
        graph.CanRedo.ShouldBeFalse();
        graph.WorkflowNames.ShouldContain("diagnostics");
        graph.WorkflowNames.ShouldNotContain("commands");
        Should.Throw<ArgumentException>(() => graph.AddWorkflow("diagnostics"));
        Should.Throw<ArgumentException>(() => graph.AddWorkflow("invalid.name"));
    }

    private const string Sample = """
    {
      "Logging": { "KeepThis": true },
      "FluxFlow": {
        "Resources": { "Custom": { "Type": "external", "Value": [1,"1",null] } },
        "Workflows": {
          "main": {
            "Resources": { "Local": { "Type": "external" } },
            "clock": { "type": "scheduler", "interval": "00:00:01", "Output": [
              "map.Input", {"address":"map.Input", "condition":"Value > 0", "engine":"custom"}
            ], "Events": "other.sink.Input", "Extra": { "PascalCase": 123 } },
            "map": { "Type": "mapper", "Engine": "jsonata", "Expression": "$", "Error": "sink.Input" },
            "sink": { "Type": "batch", "BatchSize": 2, "MaxWait": "00:00:01", "Input": "clock.Output" },
            "custom": { "Type": "custom", "Input": { "Future": true }, "Values": [false, null, 1] }
          },
          "other": { "sink": { "Type": "batch", "BatchSize": 1, "MaxWait": "00:00:01" } }
        }
      }
    }
    """;

    [Fact]
    public void Repeated_canvas_saves_preserve_json_values_casing_unknown_content_and_link_forms()
    {
        var graph = StudioTestServices.CreateGraph();
        graph.LoadJson(Sample, "FluxFlow").Count.ShouldBeGreaterThan(0);
        JsonNode.DeepEquals(JsonNode.Parse(Sample), JsonNode.Parse(graph.ToJson())).ShouldBeTrue();
        JsonNode.DeepEquals(JsonNode.Parse(Sample), JsonNode.Parse(graph.ToJson())).ShouldBeTrue();
        graph.SelectWorkflow("other");
        JsonNode.DeepEquals(JsonNode.Parse(Sample), JsonNode.Parse(graph.ToJson())).ShouldBeTrue();
    }

    [Fact]
    public void Inspector_edit_preserves_unrelated_configuration_and_property_spelling()
    {
        var graph = StudioTestServices.CreateGraph();
        graph.LoadJson(Sample, "FluxFlow");
        var clock = graph.Diagram.Nodes.OfType<FlowNodeModel>().Single(node => node.NodeName == "clock");
        clock.Configuration["interval"] = JsonSerializer.SerializeToElement("00:00:02");
        var expected = JsonNode.Parse(Sample)!;
        expected["FluxFlow"]!["Workflows"]!["main"]!["clock"]!["interval"] = "00:00:02";
        JsonNode.DeepEquals(expected, JsonNode.Parse(graph.ToJson())).ShouldBeTrue();
    }

    [Fact]
    public void Removing_one_conditional_link_does_not_remove_unconditional_input_or_error_declarations()
    {
        var graph = StudioTestServices.CreateGraph();
        graph.LoadJson(Sample, "FluxFlow");
        var conditional = graph.Diagram.Links.OfType<FlowLinkModel>().Single(link => link.Original.Condition is not null);
        graph.Diagram.Links.Remove(conditional);
        var expected = JsonNode.Parse(Sample)!;
        expected["FluxFlow"]!["Workflows"]!["main"]!["clock"]!["Output"]!.AsArray().RemoveAt(1);
        JsonNode.DeepEquals(expected, JsonNode.Parse(graph.ToJson())).ShouldBeTrue();
        JsonNode.DeepEquals(expected, JsonNode.Parse(graph.ToJson())).ShouldBeTrue();
    }

    [Theory]
    [InlineData("clock", "Output")]
    [InlineData("map", "Error")]
    [InlineData("sink", "Input")]
    public void Removing_a_rendered_declaration_preserves_other_links(string owner, string property)
    {
        var graph = StudioTestServices.CreateGraph();
        graph.LoadJson(Sample, "FluxFlow");
        var link = graph.Diagram.Links.OfType<FlowLinkModel>().First(item => item.Original.Owner.ComponentName == owner &&
            item.Original.Property == property && item.Original.Condition is null);
        graph.Diagram.Links.Remove(link);
        var updated = new DesignerConfiguration(graph.ToJson(), "FluxFlow");
        updated.Links(StudioTestServices.CreateGraph().FindPresentation).Count.ShouldBe(4);
    }

    [Fact]
    public void Malformed_addresses_and_duplicate_properties_leave_current_canvas_unchanged()
    {
        var graph = StudioTestServices.CreateGraph();
        graph.LoadJson(Sample, "FluxFlow");
        Should.Throw<JsonException>(() => graph.LoadJson("""{"Workflows":{"main":{"s":{"Type":"source","Output":"bad"}}}}"""));
        Should.Throw<FormatException>(() => graph.LoadJson("""{"Workflows":{"main":{"s":{"Type":"source","type":"mapper"}}}}"""));
        JsonNode.DeepEquals(JsonNode.Parse(Sample), JsonNode.Parse(graph.ToJson())).ShouldBeTrue();
    }

    [Fact]
    public async Task New_connections_export_current_configuration_and_register_in_native_DI_without_activation()
    {
        var graph = StudioTestServices.CreateGraph();
        var clock = graph.AddNode(graph.FindDescription("scheduler")!);
        clock.Configuration["Interval"] = JsonSerializer.SerializeToElement("00:00:01");
        var mapper = graph.AddNode(graph.FindDescription("mapper")!);
        mapper.Configuration["Engine"] = JsonSerializer.SerializeToElement("not-loaded-in-the-editor");
        mapper.Configuration["Expression"] = JsonSerializer.SerializeToElement("file:missing.jsonata");
        graph.Diagram.Links.Add(new Blazor.Diagrams.Core.Models.LinkModel(clock.Ports[0], mapper.Ports[0]));
        var json = graph.ToJson();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        using var configuration = (ConfigurationRoot)new ConfigurationBuilder().AddJsonStream(stream).Build();
        var services = new ServiceCollection();
        services.AddScheduler().AddMapper().AddFluxFlowApplication(configuration);
        await using var provider = services.BuildServiceProvider();
        provider.GetComponentAddresses().Count().ShouldBe(2);
        provider.GetOutputPortAddresses().Count().ShouldBe(4);
        JsonNode.Parse(json)!["Workflows"]!["main"]![clock.NodeName]!["Output"]!.GetValue<string>()
            .ShouldBe(ComponentAddress.Create("main", mapper.NodeName).Port("Input").Value);
        JsonNode.DeepEquals(JsonNode.Parse(json), JsonNode.Parse(graph.ToJson())).ShouldBeTrue();
    }

    [Fact]
    public async Task Presentation_ports_match_current_registrations_without_fabricating_scheduler_input()
    {
        const string json = """
        {"Workflows":{"main":{
          "scheduler":{"Type":"scheduler","Interval":"00:00:01"},
          "source":{"Type":"source","Values":"[1]"},
          "mapper":{"Type":"mapper","Engine":"custom","Expression":"$"},
          "batch":{"Type":"batch","BatchSize":2,"MaxWait":"00:00:01"},
          "gate":{"Type":"gate"}
        }}}
        """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        using var config = (ConfigurationRoot)new ConfigurationBuilder().AddJsonStream(stream).Build();
        var services = new ServiceCollection();
        services.AddScheduler().AddSource().AddMapper().AddBatch().AddGate().AddFluxFlowApplication(config);
        await using var provider = services.BuildServiceProvider();
        foreach (var item in StudioTestServices.CreateGraph().Components.Select(description => description.Palette)
            .Where(item => config.GetSection("Workflows:main:" + item.ComponentType).Exists()))
        {
            var address = ComponentAddress.Create("main", item.ComponentType);
            provider.GetInputPortAddresses().Where(port => port.ComponentAddress == address).Select(port => port.PortName).Order()
                .ShouldBe(item.Inputs.Select(port => port.Name).Order());
            provider.GetOutputPortAddresses().Where(port => port.ComponentAddress == address).Select(port => port.PortName).Order()
                .ShouldBe(item.Outputs.Select(port => port.Name).Order());
        }
        var graph = StudioTestServices.CreateGraph();
        graph.LoadJson(json);
        graph.Diagram.Nodes.OfType<FlowNodeModel>().Single(node => node.ComponentType == "scheduler").InputPortNames.ShouldBeEmpty();
    }

    [Fact]
    public void New_link_appends_without_rewriting_existing_condition_or_reference()
    {
        var graph = StudioTestServices.CreateGraph();
        graph.LoadJson(Sample, "FluxFlow");
        var clock = graph.Diagram.Nodes.OfType<FlowNodeModel>().Single(node => node.NodeName == "clock");
        var sink = graph.Diagram.Nodes.OfType<FlowNodeModel>().Single(node => node.NodeName == "sink");
        graph.Diagram.Links.Add(new Blazor.Diagrams.Core.Models.LinkModel(clock.Ports[0], sink.Ports[0]));
        var expected = JsonNode.Parse(Sample)!;
        expected["FluxFlow"]!["Workflows"]!["main"]!["clock"]!["Output"]!.AsArray().Add("main.sink.Input");
        JsonNode.DeepEquals(expected, JsonNode.Parse(graph.ToJson())).ShouldBeTrue();
        JsonNode.DeepEquals(expected, JsonNode.Parse(graph.ToJson())).ShouldBeTrue();
    }

    [Theory]
    [InlineData("Input")]
    [InlineData("Error")]
    public void Retargeting_preserves_declaration_direction_condition_and_engine(string property)
    {
        var document = new DesignerConfiguration("""
        {"Workflows":{"main":{
          "a":{"Type":"source"},"b":{"Type":"mapper"},"c":{"Type":"mapper"}
        }}}
        """);
        var owner = ComponentAddress.Create("main", "b");
        var node = document.Nodes("main").Single(item => item.Address == owner);
        node.Values[property] = JsonSerializer.SerializeToElement(new { Address = property == "Input" ? "a.Output" : "c.Input", Condition = "test", Engine = "custom" });
        document.UpdateNode(node);
        var old = document.Links(StudioTestServices.CreateGraph().FindPresentation).Single();
        document.RemoveLink(old);
        document.AddLink(old.Source, ComponentAddress.Create("main", "a").Port("Input"), old);
        var current = document.Links(StudioTestServices.CreateGraph().FindPresentation).Single();
        current.Property.ShouldBe(property);
        current.IsInput.ShouldBe(property == "Input");
        current.IsError.ShouldBe(property == "Error");
        current.Condition.ShouldBe("test");
        current.Engine.ShouldBe("custom");
        current.Target.Value.ShouldBe("main.a.Input");
    }

    [Theory]
    [InlineData("input", false)]
    [InlineData("output", false)]
    [InlineData("error", true)]
    public void Reconnecting_preserves_object_form_and_property_spelling(string property, bool conditional)
    {
        var root = JsonNode.Parse("""
        {"Workflows":{"main":{
          "a":{"Type":"mapper"},"b":{"Type":"mapper"},"c":{"Type":"mapper"}
        }}}
        """)!;
        var declaration = new JsonObject { ["address"] = property == "input" ? "a.Output" : "c.Input" };
        if (conditional)
        {
            declaration["condition"] = "test";
            declaration["engine"] = "custom";
        }
        root["Workflows"]!["main"]!["b"]![property] = declaration;
        var document = new DesignerConfiguration(root.ToJsonString());
        var old = document.Links(StudioTestServices.CreateGraph().FindPresentation).Single();
        document.RemoveLink(old);
        document.AddLink(old.Source, ComponentAddress.Create("main", "a").Port("Input"), old);

        root["Workflows"]!["main"]!["b"]!.AsObject().Remove(property);
        declaration["address"] = property == "input" ? "main.a.Output" : "main.a.Input";
        root["Workflows"]!["main"]![property == "input" ? "a" : "b"]![property] = declaration;
        JsonNode.DeepEquals(root, JsonNode.Parse(document.ToJson())).ShouldBeTrue();
    }

    [Fact]
    public void Generated_names_do_not_collide_with_loaded_or_unknown_nodes()
    {
        var graph = StudioTestServices.CreateGraph();
        graph.LoadJson("""{"Workflows":{"main":{"mapper-1":{"Type":"custom"}}}}""");
        graph.AddNode(graph.FindDescription("mapper")!).NodeName.ShouldBe("mapper-2");
        graph.AddNode(graph.FindDescription("mapper")!).NodeName.ShouldBe("mapper-3");
    }

    [Theory]
    [InlineData("Output", false, false)]
    [InlineData("Output", true, true)]
    [InlineData("Events", true, false)]
    [InlineData("Error", false, true)]
    [InlineData("Error", true, false)]
    public async Task Mirrored_handles_save_logical_ports_and_error_filter_with_undo_redo(
        string name, bool sourceOnLeft, bool startAtInput)
    {
        var graph = StudioTestServices.CreateGraph();
        graph.LoadJson("""{"Workflows":{"main":{"a":{"Type":"mapper","Engine":"jsonata","Expression":"$"},"b":{"Type":"mapper","Engine":"jsonata","Expression":"$"}}}}""");
        graph.ResetHistory();
        var before = graph.ToJson();
        var a = graph.Diagram.Nodes.OfType<FlowNodeModel>().Single(node => node.NodeName == "a");
        var b = graph.Diagram.Nodes.OfType<FlowNodeModel>().Single(node => node.NodeName == "b");
        var source = Handle(a, name, sourceOnLeft);
        var target = Handle(b, "Input", !sourceOnLeft);
        source.CanAttachTo(target).ShouldBeTrue();
        source.CanAttachTo(Handle(b, "Output", true)).ShouldBeFalse();
        target.CanAttachTo(Handle(a, "Input", false)).ShouldBeFalse();
        var link = new Blazor.Diagrams.Core.Models.LinkModel(startAtInput ? target : source, startAtInput ? source : target);
        graph.Diagram.Links.Add(link);
        link.TargetMarker.ShouldBeSameAs(Blazor.Diagrams.Core.Models.LinkMarker.Arrow);
        link.Color.ShouldBe("currentColor");
        link.SelectedColor.ShouldBe(link.Color);
        link.TriggerTargetAttached();
        graph.RecordEdit();
        var saved = graph.ToJson();
        JsonNode.Parse(saved)!["Workflows"]!["main"]!["a"]![name]!.GetValue<string>().ShouldBe("main.b.Input");
        var loaded = graph.Diagram.Links.OfType<FlowLinkModel>().Single();
        loaded.TargetMarker.ShouldBeSameAs(Blazor.Diagrams.Core.Models.LinkMarker.Arrow);
        loaded.Color.ShouldBe("currentColor");
        loaded.SelectedColor.ShouldBe(loaded.Color);
        loaded.Original.Source.PortName.ShouldBe(name == "Error" ? "Output" : name);
        loaded.Original.IsError.ShouldBe(name == "Error");
        ((FlowPortModel)((Blazor.Diagrams.Core.Anchors.SinglePortAnchor)loaded.Source).Port).Name.ShouldBe(name);
        ((Blazor.Diagrams.Core.Anchors.SinglePortAnchor)loaded.Source).Port.Alignment.ShouldBe(source.Alignment);
        ((Blazor.Diagrams.Core.Anchors.SinglePortAnchor)loaded.Target).Port.Alignment.ShouldBe(target.Alignment);
        graph.Undo();
        JsonNode.DeepEquals(JsonNode.Parse(before), JsonNode.Parse(graph.ToJson())).ShouldBeTrue();
        graph.Redo();
        JsonNode.DeepEquals(JsonNode.Parse(saved), JsonNode.Parse(graph.ToJson())).ShouldBeTrue();
        loaded = graph.Diagram.Links.OfType<FlowLinkModel>().Single();
        loaded.TargetMarker.ShouldBeSameAs(Blazor.Diagrams.Core.Models.LinkMarker.Arrow);
        loaded.SelectedColor.ShouldBe(loaded.Color);
        ((FlowPortModel)((SinglePortAnchor)loaded.Source).Port).ColorRole.ShouldBe(
            name == "Error" ? "error" : name == "Events" ? "events" : "output");
        ((Blazor.Diagrams.Core.Anchors.SinglePortAnchor)loaded.Source).Port.Alignment.ShouldBe(source.Alignment);
        ((Blazor.Diagrams.Core.Anchors.SinglePortAnchor)loaded.Target).Port.Alignment.ShouldBe(target.Alignment);

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(saved));
        using var config = (ConfigurationRoot)new ConfigurationBuilder().AddJsonStream(stream).Build();
        var services = new ServiceCollection();
        services.AddMapper().AddFluxFlowApplication(config);
        await using var provider = services.BuildServiceProvider();
        provider.GetOutputPortAddresses().Count().ShouldBe(4); // Error is not a physical port.
    }

    [Fact]
    public void Switching_output_and_error_preserves_conditions_unknown_fields_and_history()
    {
        var graph = StudioTestServices.CreateGraph();
        const string json = """
        {"Workflows":{"main":{
          "a":{"Type":"mapper","error":{"address":"b.Input","condition":"test","engine":"custom"},"Extra":{"Keep":true}},
          "b":{"Type":"mapper"}
        }}}
        """;
        graph.LoadJson(json);
        graph.ResetHistory();
        var link = graph.Diagram.Links.OfType<FlowLinkModel>().Single();
        var a = graph.Diagram.Nodes.OfType<FlowNodeModel>().Single(node => node.NodeName == "a");
        link.SetSource(new Blazor.Diagrams.Core.Anchors.SinglePortAnchor(Handle(a, "Output", true)));
        graph.RecordEdit();
        var output = graph.Diagram.Links.OfType<FlowLinkModel>().Single().Original;
        output.IsError.ShouldBeFalse();
        output.Condition.ShouldBe("test");
        output.Engine.ShouldBe("custom");
        var expected = JsonNode.Parse(json)!;
        var owner = expected["Workflows"]!["main"]!["a"]!.AsObject();
        var declaration = owner["error"]!.DeepClone();
        owner.Remove("error");
        declaration["address"] = "main.b.Input";
        owner["Output"] = declaration;
        JsonNode.DeepEquals(expected, JsonNode.Parse(graph.ToJson())).ShouldBeTrue();
        graph.Undo();
        JsonNode.DeepEquals(JsonNode.Parse(json), JsonNode.Parse(graph.ToJson())).ShouldBeTrue();
        graph.Redo();
        JsonNode.DeepEquals(expected, JsonNode.Parse(graph.ToJson())).ShouldBeTrue();
        link = graph.Diagram.Links.OfType<FlowLinkModel>().Single();
        a = graph.Diagram.Nodes.OfType<FlowNodeModel>().Single(node => node.NodeName == "a");
        link.SetSource(new Blazor.Diagrams.Core.Anchors.SinglePortAnchor(Handle(a, "Error", false)));
        graph.RecordEdit();
        graph.Diagram.Links.OfType<FlowLinkModel>().Single().Original.IsError.ShouldBeTrue();
        graph.Diagram.Links.OfType<FlowLinkModel>().Single().Original.Condition.ShouldBe("test");
    }

    private static FlowPortModel Handle(FlowNodeModel node, string name, bool left) => node.Ports.OfType<FlowPortModel>()
        .Single(port => port.Name == name && port.Alignment == (left
            ? Blazor.Diagrams.Core.Models.PortAlignment.Left : Blazor.Diagrams.Core.Models.PortAlignment.Right));

    [Fact]
    public void Node_color_is_editor_only_and_survives_history_workflow_switches_and_configuration_edits()
    {
        var graph = StudioTestServices.CreateGraph();
        graph.LoadJson(Sample, "FluxFlow");
        graph.ResetHistory();
        var node = graph.Diagram.Nodes.OfType<FlowNodeModel>().Single(item => item.NodeName == "sink");
        graph.Diagram.SelectModel(node, true);
        graph.SetSelectedNodeColor("#8247b5");
        node.AccentColor.ShouldBe("#8247b5");
        JsonNode.DeepEquals(JsonNode.Parse(Sample), JsonNode.Parse(graph.ToJson())).ShouldBeTrue();
        graph.Undo();
        graph.SelectedNode!.NodeName.ShouldBe("sink");
        graph.SelectedNode.Color.ShouldBeNull();
        graph.SelectedNode.AccentColor.ShouldBe(graph.SelectedNode.DefaultColor);
        graph.Redo();
        graph.SelectedNode!.Color.ShouldBe("#8247b5");
        graph.SelectedNode.Configuration["BatchSize"] = JsonSerializer.SerializeToElement(3);
        graph.RecordEdit();
        graph.SelectedNode!.Color.ShouldBe("#8247b5");
        graph.Undo();
        JsonNode.DeepEquals(JsonNode.Parse(Sample), JsonNode.Parse(graph.ToJson())).ShouldBeTrue();
        graph.SelectWorkflow("other");
        graph.Diagram.Nodes.OfType<FlowNodeModel>().Single().Color.ShouldBeNull();
        graph.SelectWorkflow("main");
        node = graph.Diagram.Nodes.OfType<FlowNodeModel>().Single(item => item.NodeName == "sink");
        node.Color.ShouldBe("#8247b5");
        graph.Diagram.SelectModel(node, true);
        graph.SetSelectedNodeColor(null);
        graph.SelectedNode!.Color.ShouldBeNull();
        graph.Undo();
        graph.SelectedNode!.Color.ShouldBe("#8247b5");
        graph.DeleteSelected();
        graph.RecordEdit();
        graph.Undo();
        graph.Diagram.Nodes.OfType<FlowNodeModel>().Single(item => item.NodeName == "sink").Color.ShouldBe("#8247b5");
        graph.LoadJson(Sample, "FluxFlow");
        graph.Diagram.Nodes.OfType<FlowNodeModel>().ShouldAllBe(item => item.Color == null);
    }

    [Fact]
    public void Clearing_canvas_retains_unknown_components_resources_and_other_workflows()
    {
        var graph = StudioTestServices.CreateGraph();
        graph.LoadJson(Sample, "FluxFlow");
        graph.Clear();
        var result = JsonNode.Parse(graph.ToJson())!;
        result["FluxFlow"]!["Workflows"]!["main"]!.AsObject().Select(item => item.Key).ShouldBe(["Resources", "custom"]);
        JsonNode.DeepEquals(result["FluxFlow"]!["Workflows"]!["other"], JsonNode.Parse(Sample)!["FluxFlow"]!["Workflows"]!["other"]).ShouldBeTrue();
    }
}
