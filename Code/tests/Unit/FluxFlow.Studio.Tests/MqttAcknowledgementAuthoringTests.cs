using System.Text.Json;
using System.Text.Json.Nodes;
using FluxFlow.Studio.UI.Features.Designer.Canvas;
using FluxFlow.Plugins.Mqtt;
using FluxFlow.Studio.UI.Features.Plugins;
using Shouldly;
using Xunit;

namespace FluxFlow.Studio.Tests;

public sealed class MqttAcknowledgementAuthoringTests
{
    [Theory]
    [InlineData("\"Automatic\"", 0, false)]
    [InlineData("\"Manual\"", 1, true)]
    [InlineData("\"manual\"", 1, true)]
    [InlineData("1", 1, true)]
    [InlineData("\" Manual \"", 1, true)]
    [InlineData("\"01\"", 1, true)]
    [InlineData("\"future-mode\"", 0, false)]
    public void Topic_defaults_and_unused_ack_visibility_follow_mode_without_editing_saved_values(string mode, int qos, bool visible)
    {
        var graph = StudioTestServices.CreateGraph();
        graph.LoadJson("{\"Workflows\":{\"main\":{\"receive\":{\"Type\":\"mqtt.subscriber\",\"acknowledgementMode\":" + mode + ",\"Topics\":[{\"Topic\":\"keep\",\"QoS\":2}],\"MaxPendingAcknowledgements\":7}}}}");
        var before = graph.ToProjectJson();
        var node = graph.Diagram.Nodes.OfType<FlowNodeModel>().Single();
        MqttTopics.NewTopicQoS(node.Configuration).ShouldBe(qos);
        MqttTopics.QoS(new MqttTopics().Read(node.Configuration).Single()).ShouldBe(2);
        node.Ports.OfType<FlowPortModel>().Where(port => port.Name == "ACK").ShouldAllBe(port => port.IsVisible == visible);
        node.InputPortNames.ShouldContain("ACK");
        graph.ToProjectJson().ShouldBe(before);
    }

    [Theory]
    [InlineData("settle.Output", true)]
    [InlineData("missing.Output", false)]
    [InlineData("other.settle.Output", false)]
    public void Automatic_ack_retains_wiring_and_history_even_when_the_source_is_not_on_canvas(string source, bool rendered)
    {
        var graph = StudioTestServices.CreateGraph();
        graph.LoadJson("{\"Workflows\":{\"main\":{\"receive\":{\"Type\":\"mqtt.subscriber\",\"AcknowledgementMode\":\"Manual\",\"ACK\":\"" + source + "\",\"Topics\":\"keep/#\",\"MaxPendingAcknowledgements\":7},\"settle\":{\"Type\":\"mapper\"}},\"other\":{\"settle\":{\"Type\":\"mapper\"}}}}", workflow: "main");
        graph.ResetHistory();
        var before = graph.ToProjectJson();
        var node = graph.Diagram.Nodes.OfType<FlowNodeModel>().Single(node => node.NodeName == "receive");
        node.Configuration["AcknowledgementMode"] = JsonSerializer.SerializeToElement("Automatic");
        graph.RecordEdit();
        var after = graph.ToProjectJson();
        AssertInactive();
        graph.Undo(); graph.ToProjectJson().ShouldBe(before);
        graph.Diagram.Nodes.OfType<FlowNodeModel>().Single(node => node.NodeName == "receive").Ports.OfType<FlowPortModel>()
            .Where(port => port.Name == "ACK").ShouldAllBe(port => port.IsVisible && port.InactiveReason == null);
        graph.Redo(); graph.ToProjectJson().ShouldBe(after);
        AssertInactive();
        graph.LoadProject(after);
        AssertInactive();
        if (rendered)
        {
            graph.Diagram.Links.Clear();
            graph.RecordEdit();
            graph.Diagram.Nodes.OfType<FlowNodeModel>().Single(node => node.NodeName == "receive").Ports.OfType<FlowPortModel>()
                .Where(port => port.Name == "ACK").ShouldAllBe(port => !port.IsVisible);
            graph.Undo();
            graph.ToProjectJson().ShouldBe(after);
            AssertInactive();
        }

        void AssertInactive()
        {
            var receiver = graph.Diagram.Nodes.OfType<FlowNodeModel>().Single(node => node.NodeName == "receive");
            var ack = receiver.Ports.OfType<FlowPortModel>().Where(port => port.Name == "ACK").ToArray();
            ack.ShouldAllBe(port => port.IsVisible && port.InactiveReason == "Requires Manual broker acknowledgement.");
            ack.Any(port => port.Links.Count > 0).ShouldBe(rendered);
            var output = graph.Diagram.Nodes.OfType<FlowNodeModel>().Single(node => node.NodeName == "settle").Ports.OfType<FlowPortModel>().First(port => !port.IsInput);
            ack[0].CanAttachTo(output).ShouldBeFalse();
            output.CanAttachTo(ack[0]).ShouldBeFalse();
            receiver.Configuration["ACK"].GetString().ShouldBe(source);
            receiver.Configuration["Topics"].GetString().ShouldBe("keep/#");
            receiver.Configuration["MaxPendingAcknowledgements"].GetInt32().ShouldBe(7);
        }
    }

    [Theory]
    [InlineData("\"sensors/#\"")]
    [InlineData("{\"topic\":\"sensors/#\",\"qos\":\"0\",\"Extension\":42}")]
    [InlineData("[\"sensors/#\",{\"Topic\":\"other\",\"QoS\":2,\"Extension\":true}]")]
    public void Topic_edits_preserve_untouched_data_and_undo_redo(string topicsJson)
    {
        var graph = StudioTestServices.CreateGraph();
        graph.LoadJson("{\"Workflows\":{\"main\":{\"receive\":{\"Type\":\"mqtt.subscriber\",\"Connection\":\"broker\",\"topics\":" + topicsJson + ",\"Other\":\"keep\"}}}}");
        graph.ResetHistory();
        var before = graph.ToProjectJson();
        var node = graph.Diagram.Nodes.OfType<FlowNodeModel>().Single();
        var topics = new MqttTopics().Read(node.Configuration).ToList();
        MqttTopics.Edit(topics[0], MqttTopics.Topic(topics[0]), MqttTopics.QoS(topics[0])!.Value).GetRawText().ShouldBe(topics[0].GetRawText());
        graph.ToProjectJson().ShouldBe(before);
        var untouched = topics.Skip(1).Select(item => item.GetRawText()).ToArray();
        topics[0] = MqttTopics.Edit(topics[0], "devices/+/state", 1);
        new MqttTopics().Write(node.Configuration, topics);
        graph.RecordEdit();
        node.Configuration.ContainsKey("Topics").ShouldBeFalse();
        var edited = new MqttTopics().Read(node.Configuration);
        MqttTopics.Topic(edited[0]).ShouldBe("devices/+/state");
        MqttTopics.QoS(edited[0]).ShouldBe(1);
        edited.Skip(1).Select(item => item.GetRawText()).ShouldBe(untouched);
        if (topicsJson.Contains("Extension\":42")) edited[0].GetProperty("Extension").GetInt32().ShouldBe(42);
        var after = graph.ToProjectJson();
        graph.Undo(); graph.ToProjectJson().ShouldBe(before);
        graph.Redo(); graph.ToProjectJson().ShouldBe(after);
        node = graph.Diagram.Nodes.OfType<FlowNodeModel>().Single();
        new MqttTopics().Write(node.Configuration, []);
        graph.RecordEdit();
        new MqttTopics().Read(node.Configuration).ShouldBeEmpty();
        graph.Undo(); graph.ToProjectJson().ShouldBe(after);
        var reopened = StudioTestServices.CreateGraph();
        reopened.LoadProject(after);
        JsonNode.DeepEquals(JsonNode.Parse(reopened.ToJson()), JsonNode.Parse(graph.ToJson())).ShouldBeTrue();
    }

    [Fact]
    public void Manual_ack_settings_and_links_preserve_history_and_existing_configuration()
    {
        const string original = """
            {"Extra":{"Keep":true},"Workflows":{"main":{
              "legacy":{"Type":"mqtt.subscriber","Connection":"old","Topics":"legacy/#","Unknown":42},
              "receive":{"Type":"mqtt.subscriber","Connection":"broker","Topics":[{"Topic":"work","QoS":1}],
                "AcknowledgementMode":"Manual","MaxPendingAcknowledgements":4,"ACK":"settle.Output","Unknown":"keep"},
              "settle":{"Type":"mapper","Engine":"jsonata","Expression":"$"}}}}
            """;
        var graph = StudioTestServices.CreateGraph();
        graph.LoadJson(original);
        graph.ResetHistory();
        var before = graph.ToProjectJson();
        var node = graph.Diagram.Nodes.OfType<FlowNodeModel>().Single(n => n.NodeName == "receive");
        node.InputPortNames.ShouldBe(["Input", "ACK"]);
        node.Description.GetInspector(node.Configuration).Sections.SelectMany(section => section.Options)
            .ShouldContain(option => option.Name == "MaxPendingAcknowledgements");
        node.Configuration["MaxPendingAcknowledgements"] = JsonSerializer.SerializeToElement(2);
        graph.RecordEdit();
        var after = graph.ToProjectJson();
        graph.Undo(); graph.ToProjectJson().ShouldBe(before);
        graph.Redo(); graph.ToProjectJson().ShouldBe(after);
        node = graph.Diagram.Nodes.OfType<FlowNodeModel>().Single(n => n.NodeName == "receive");
        node.Configuration["AcknowledgementMode"] = JsonSerializer.SerializeToElement("Automatic");
        graph.RecordEdit();
        node.Description.GetInspector(node.Configuration).Sections.SelectMany(section => section.Options)
            .ShouldNotContain(option => option.Name == "MaxPendingAcknowledgements");
        node.Configuration["MaxPendingAcknowledgements"].GetInt32().ShouldBe(2);
        var automatic = graph.ToProjectJson();
        graph.Undo(); graph.ToProjectJson().ShouldBe(after);
        graph.Redo(); graph.ToProjectJson().ShouldBe(automatic);
        graph.Undo();
        var reopened = StudioTestServices.CreateGraph();
        reopened.LoadProject(after);
        var saved = JsonNode.Parse(reopened.ToJson())!;
        saved["Workflows"]!["main"]!["legacy"]!.ToJsonString().ShouldBe(JsonNode.Parse(original)!["Workflows"]!["main"]!["legacy"]!.ToJsonString());
        saved["Workflows"]!["main"]!["receive"]!["Unknown"]!.GetValue<string>().ShouldBe("keep");
        JsonNode.DeepEquals(saved, JsonNode.Parse(graph.ToJson())).ShouldBeTrue();
    }
}
