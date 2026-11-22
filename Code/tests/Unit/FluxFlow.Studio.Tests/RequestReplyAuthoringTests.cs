using System.Text.Json;
using System.Text.Json.Nodes;
using FluxFlow.Studio.UI.Features.Designer.Canvas;
using Shouldly;
using Xunit;

namespace FluxFlow.Studio.Tests;

public sealed class RequestReplyAuthoringTests
{
    [Fact]
    public void Settings_and_feedback_links_survive_history_and_reopen_without_rewriting_rate_limit()
    {
        const string original = """
            {"Extra":{"Keep":true},"Workflows":{"main":{
              "old":{"Type":"rateLimit","Limiter":"shared","Extra":42},
              "exchange":{"Type":"requestReply","MaxInFlight":3,"BufferCapacity":7,"ReplyTimeout":"00:00:04",
                "TimeoutBehavior":"Continue","Request":"worker.Input","ACK":"worker.Output","NACK":"worker.Error","Unknown":"keep"},
              "worker":{"Type":"mapper","Engine":"jsonata","Expression":"$"}}}}
            """;
        var graph = StudioTestServices.CreateGraph();
        graph.LoadJson(original);
        graph.ResetHistory();
        var before = graph.ToProjectJson();
        var node = graph.Diagram.Nodes.OfType<FlowNodeModel>().Single(item => item.NodeName == "exchange");
        node.InputPortNames.ShouldBe(["Input", "ACK", "NACK"]);
        node.Configuration["MaxInFlight"] = JsonSerializer.SerializeToElement(1);
        graph.RecordEdit();
        var after = graph.ToProjectJson();
        graph.Undo(); graph.ToProjectJson().ShouldBe(before);
        graph.Redo(); graph.ToProjectJson().ShouldBe(after);
        var reopened = StudioTestServices.CreateGraph();
        reopened.LoadProject(after);
        JsonNode.DeepEquals(JsonNode.Parse(reopened.ToJson()), JsonNode.Parse(graph.ToJson())).ShouldBeTrue();
        var document = JsonNode.Parse(reopened.ToJson())!;
        document["Workflows"]!["main"]!["old"]!.ToJsonString().ShouldBe(JsonNode.Parse(original)!["Workflows"]!["main"]!["old"]!.ToJsonString());
        document["Workflows"]!["main"]!["exchange"]!["Unknown"]!.GetValue<string>().ShouldBe("keep");
        var added = graph.AddNode(graph.FindDescription("requestReply")!);
        added.ComponentType.ShouldBe("requestReply");
        added.Configuration.ContainsKey("Limiter").ShouldBeFalse();
    }
}
