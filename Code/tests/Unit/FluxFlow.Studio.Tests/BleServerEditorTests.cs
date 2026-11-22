using FluxFlow.Plugins.Bluetooth.Design;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluxFlow.Components.Bluetooth;
using FluxFlow.Plugins.Hierarchies;
using Shouldly;
using Xunit;

namespace FluxFlow.Studio.Tests;

public sealed class BleServerEditorTests
{
    [Fact]
    public void Standard_templates_round_trip_with_legacy_data_and_graph_history()
    {
        var graph = StudioTestServices.CreateGraph();
        graph.LoadJson("{\"Workflows\":{\"main\":{\"server\":" + Original + "}}}");
        graph.ResetHistory();
        var node = graph.Diagram.Nodes.OfType<FluxFlow.Studio.UI.Features.Designer.Canvas.FlowNodeModel>().Single();
        var before = graph.ToProjectJson();
        var draft = new BleServerDraft(node.Configuration);
        draft.AddService(FluxFlow.Components.Bluetooth.BleStandards.BatteryTemplate());
        draft.AddService(FluxFlow.Components.Bluetooth.BleStandards.TxPowerTemplate(-20));
        var saved = BleServerDraft.FromJson(draft.ToJson()).Save();
        node.Configuration.Clear();
        foreach (var pair in saved) node.Configuration[pair.Key] = pair.Value;
        graph.RecordEdit();
        var after = graph.ToProjectJson();
        graph.Undo(); graph.ToProjectJson().ShouldBe(before);
        graph.Redo(); graph.ToProjectJson().ShouldBe(after);
        graph.LoadProject(after);
        var reopened = new BleServerDraft(graph.Diagram.Nodes.OfType<FluxFlow.Studio.UI.Features.Designer.Canvas.FlowNodeModel>().Single().Configuration);
        reopened.Services.Count.ShouldBe(3);
        reopened.Services[2].Characteristics.Single().Payload.Decode().ShouldBe(new byte[] { 236 });
        reopened.Save()["Extra"].GetProperty("Keep").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public void Multi_service_visual_json_round_trip_keeps_unknown_fields_and_binary_data()
    {
        var draft = BleServerDraft.FromJson(Original);
        var service = draft.AddService(FluxFlow.Components.Bluetooth.BleStandards.BatteryTemplate());
        service.Characteristics[0].UserDescription = "Battery";
        var descriptor = service.Characteristics[0].AddDescriptor();
        descriptor.Payload = new() { Data = "00FF42" };
        var saved = BleServerDraft.FromJson(draft.ToJson()).Save();
        saved["Services"].GetArrayLength().ShouldBe(2);
        saved["Services"][0].GetProperty("Characteristics")[0].GetProperty("Extra").GetInt32().ShouldBe(42);
        saved["Extra"].GetProperty("Keep").GetBoolean().ShouldBeTrue();
        saved.ContainsKey("serviceid").ShouldBeFalse();
        saved["Services"][1].GetProperty("Characteristics")[0].GetProperty("Descriptors")[0].GetProperty("Value").GetProperty("Data").GetString().ShouldBe("00FF42");
        var reopened = new BleServerDraft(saved);
        reopened.Services[1].Name.ShouldBe("Battery Service");
        reopened.RemoveService(reopened.Services[1]);
        reopened.Save()["Services"].GetArrayLength().ShouldBe(1);
    }
    [Fact]
    public void Portable_hierarchy_keeps_selection_and_exact_bytes_across_visual_and_json()
    {
        var doc = BleGattDocument.Editor.Create();
        doc.Read(Original);
        doc.Actions.Single(a => a.Label == "Battery Level · Read and notify").Invoke();
        doc.Nodes.Last().Select();
        var selected = doc.Nodes.Single(n => n.Selected).Detail;
        doc.Fields.OfType<DocumentAction>().Single(a => a.Label == "Add custom descriptor").Invoke();
        var descriptor = doc.Fields.OfType<DocumentGroup>().Single();
        descriptor.Fields.OfType<DocumentBinary>().Single().Change(new("Hex", "00FF42"));
        doc.Read(doc.ToJson());
        doc.Nodes.Single(n => n.Selected).Detail.ShouldBe(selected);
        var saved = BleServerDraft.FromJson(doc.Save());
        saved.Services.Last().Characteristics.Single().Descriptors.Single().Payload.Decode().ShouldBe(new byte[] { 0, 255, 66 });
        saved.Save()["Extra"].GetProperty("Keep").GetBoolean().ShouldBeTrue();

        var before = doc.ToJson();
        Should.Throw<JsonException>(() => doc.Read("{"));
        doc.ToJson().ShouldBe(before);
        doc.Nodes.Single(n => n.Selected).Detail.ShouldBe(selected);
    }

    private const string Original = """
        {"Type":"ble.server","serviceid":"3f63b38e-98df-4a52-a9f8-76b803a934b2","Extra":{"Keep":true},
         "characteristics":[{"id":"f50d7529-4e5d-4088-b88f-e236cd1e7f29","value":"hello","Extra":42}]}
        """;
    private static Dictionary<string, JsonElement> Configuration() => JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(Original)!;

    [Fact]
    public void Editing_one_descriptor_changes_only_its_data_after_a_mode_round_trip()
    {
        const string original = """
            {"Type":"ble.server","OutputCapacity":"0064","IsDiscoverable":"false","IsConnectable":"TRUE",
             "Services":[{"Id":"11111111-1111-4111-8111-111111111111","Characteristics":[
              {"Id":"22222222-2222-4222-8222-222222222222","Properties":2,"InitialValue":{"Encoding":"base64","Data":"AP8="},
               "Descriptors":[{"Id":"33333333-3333-4333-8333-333333333333","Value":{"eNcoding":1,"dAta":"AP8=","Extension":42}},
                              {"Id":"44444444-4444-4444-8444-444444444444"}]}]},
              {"Id":"55555555-5555-4555-8555-555555555555","Characteristics":[
              {"Id":"66666666-6666-4666-8666-666666666666","value":"untouched"}]}]}
            """;
        var draft = BleServerDraft.FromJson(original);
        var descriptor = draft.Services[0].Characteristics[0].Descriptors[0];
        draft.Services[0].Characteristics[0].Descriptors[1].Payload.Encoding.ShouldBe(BleValueEncoding.Hex);
        descriptor.Payload = descriptor.Payload with { Data = "/wA=" };
        var reopened = BleServerDraft.FromJson(draft.ToJson());
        reopened.Services[0].Characteristics[0].Descriptors[0].Payload.Decode().ShouldBe(new byte[] { 255, 0 });
        var expected = JsonNode.Parse(original)!;
        expected["Services"]![0]!["Characteristics"]![0]!["Descriptors"]![0]!["Value"]!["dAta"] = "/wA=";
        JsonNode.DeepEquals(JsonSerializer.SerializeToNode(reopened.Save()), expected).ShouldBeTrue();
    }

    [Fact]
    public void Removing_a_temporary_template_and_editing_legacy_text_keeps_the_saved_schema()
    {
        var draft = BleServerDraft.FromJson(Original);
        var temporary = draft.AddService(BleStandards.BatteryTemplate());
        JsonNode.Parse(draft.ToJson())!["Services"]!.AsArray().Count.ShouldBe(2);
        draft.RemoveService(temporary);
        JsonNode.DeepEquals(JsonSerializer.SerializeToNode(draft.Save()), JsonNode.Parse(Original)).ShouldBeTrue();
        var characteristic = draft.Characteristics[0];
        characteristic.Payload = characteristic.Payload with { Data = "updated" };
        var actual = JsonSerializer.SerializeToNode(BleServerDraft.FromJson(draft.ToJson()).Save());
        JsonNode.DeepEquals(actual, JsonNode.Parse(Original.Replace("hello", "updated"))).ShouldBeTrue();
    }

    [Theory]
    [InlineData("Properties", "{}")]
    [InlineData("WriteProtection", "[]")]
    public void Invalid_option_shape_is_reported_as_a_repairable_editor_error(string name, string json)
    {
        var input = JsonNode.Parse(Original)!;
        input["characteristics"]![0]![name] = JsonNode.Parse(json);
        Should.Throw<ArgumentException>(() => BleServerDraft.FromJson(input.ToJsonString())).Message.ShouldContain(name);
    }

    [Fact]
    public void Edits_are_detached_and_preserve_unknown_fields_casing_and_existing_advertising_defaults()
    {
        var original = Configuration();
        var before = JsonSerializer.Serialize(original);
        var draft = new BleServerDraft(original);
        draft.IsConnectable.ShouldBeTrue();
        draft.IsDiscoverable.ShouldBeTrue();
        draft.Characteristics[0].Value = "new value";
        var added = draft.Add();
        added.Value = "second";
        var saved = draft.Save();
        JsonSerializer.Serialize(original).ShouldBe(before);
        saved["Extra"].GetProperty("Keep").GetBoolean().ShouldBeTrue();
        saved["characteristics"][0].GetProperty("Extra").GetInt32().ShouldBe(42);
        saved["characteristics"][0].GetProperty("value").GetString().ShouldBe("new value");
        saved.ContainsKey("IsConnectable").ShouldBeFalse();
        saved.ContainsKey("IsDiscoverable").ShouldBeFalse();
        saved["characteristics"].GetArrayLength().ShouldBe(2);
    }

    [Fact]
    public void Save_validates_uuids_duplicates_utf8_length_and_last_characteristic_removal()
    {
        var draft = new BleServerDraft(Configuration());
        draft.ServiceId = "bad";
        Should.Throw<Exception>(() => draft.Save());
        draft.ServiceId = Guid.NewGuid().ToString();
        var added = draft.Add();
        added.Id = draft.Characteristics[0].Id;
        Should.Throw<ArgumentException>(() => draft.Save());
        draft.Characteristics.Remove(added);
        draft.Characteristics[0].Value = new string('\u00e9', 257);
        draft.Characteristics[0].ByteCount.ShouldBe(514);
        Should.Throw<ArgumentException>(() => draft.Save());
        draft.Characteristics.Clear();
        Should.Throw<ArgumentException>(() => draft.Save());
        draft.Add().Value = "restored";
        draft.IsDiscoverable = false;
        draft.Save()["IsDiscoverable"].GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public void Json_and_visual_edits_share_a_draft_without_losing_unknown_fields()
    {
        var draft = new BleServerDraft(Configuration());
        draft.Characteristics[0].Value = "visual edit";
        var json = draft.ToJson().Replace("visual edit", "JSON edit");
        var parsed = BleServerDraft.FromJson(json);
        parsed.Characteristics[0].Value.ShouldBe("JSON edit");
        parsed.IsDiscoverable = false;
        var saved = BleServerDraft.FromJson(parsed.ToJson()).Save();
        saved["Extra"].GetProperty("Keep").GetBoolean().ShouldBeTrue();
        saved["characteristics"][0].GetProperty("Extra").GetInt32().ShouldBe(42);
        saved["IsDiscoverable"].GetBoolean().ShouldBeFalse();
        draft.Characteristics[0].Value.ShouldBe("visual edit");
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"ServiceId\":\"a\",\"serviceid\":\"b\"}")]
    [InlineData("{\"Characteristics\":false}")]
    [InlineData("{\"IsDiscoverable\":\"yes\"}")]
    [InlineData("{\"Type\":\"ble.client\"}")]
    public void Invalid_json_configuration_is_rejected(string json)
        => Should.Throw<Exception>(() => BleServerDraft.FromJson(json));

    [Fact]
    public void Incomplete_visual_drafts_can_be_edited_as_json_but_not_saved()
    {
        var draft = BleServerDraft.FromJson("{\"Type\":\"ble.server\"}");
        Should.Throw<Exception>(() => draft.Save());
        draft.ServiceId = Guid.NewGuid().ToString();
        draft.Add().Value = "complete";
        BleServerDraft.FromJson(draft.ToJson()).Save().ShouldNotBeEmpty();
    }

    [Fact]
    public void Visual_edits_round_trip_through_existing_graph_history()
    {
        var graph = StudioTestServices.CreateGraph();
        graph.LoadJson("{\"Workflows\":{\"main\":{\"server\":" + Original + "}}}");
        graph.ResetHistory();
        var node = graph.Diagram.Nodes.OfType<FluxFlow.Studio.UI.Features.Designer.Canvas.FlowNodeModel>().Single();
        var before = graph.ToProjectJson();
        var draft = new BleServerDraft(node.Configuration);
        draft.Characteristics[0].Value = "updated";
        draft.IsConnectable = false;
        var changed = draft.Save();
        node.Configuration.Clear();
        foreach (var pair in changed) node.Configuration[pair.Key] = pair.Value;
        graph.RecordEdit();
        var after = graph.ToProjectJson();
        graph.Undo();
        graph.ToProjectJson().ShouldBe(before);
        graph.Redo();
        graph.ToProjectJson().ShouldBe(after);
        graph.LoadProject(after);
        graph.ToProjectJson().ShouldBe(after);
    }
}
