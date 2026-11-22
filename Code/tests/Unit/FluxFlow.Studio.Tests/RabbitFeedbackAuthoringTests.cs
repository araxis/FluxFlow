using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluxFlow.Application;
using FluxFlow.Configuration.Application;
using FluxFlow.Configuration.Components.Mapping;
using FluxFlow.Configuration.Components.RabbitMQ;
using FluxFlow.Studio.UI.Features.Designer.Canvas;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Studio.Tests;

public sealed class RabbitFeedbackAuthoringTests
{
    [Fact]
    public void Feedback_ports_and_policy_preserve_legacy_settlement_unknown_fields_and_history()
    {
        const string json = """
            {"Resources":{"broker":{"Type":"amqp.connection","Endpoint":"amqp://localhost"}},"Workflows":{"main":{
              "receive":{"Type":"amqp.consumer","Connection":"broker","Queue":"orders","Prefetch":7,"RequeueOnNack":true,
                "ACK":"success.Output","NACK":"failure.Output","Input":"legacy.Output","Output":"success.Input","Extra":42},
              "success":{"Type":"mapper","Engine":"jsonata","Expression":"$"},
              "failure":{"Type":"mapper","Engine":"jsonata","Expression":"$"},
              "legacy":{"Type":"mapper","Engine":"jsonata","Expression":"$"},
              "settle":{"Type":"amqp.settlement","Consumer":"receive"}}}}
            """;
        using var config = (ConfigurationRoot)new ConfigurationBuilder().AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(json))).Build();
        using var provider = new ServiceCollection().AddRabbitMQ().AddMapper().AddFluxFlowApplication(config).BuildServiceProvider();
        provider.GetInputPortAddresses().Where(port => port.ComponentAddress.Value == "main.receive")
            .Select(port => port.PortName).Order().ShouldBe(new[] { "ACK", "Input", "NACK" });
        var graph = StudioTestServices.CreateGraph();
        graph.LoadJson(json); graph.ResetHistory();
        var before = graph.ToProjectJson();
        JsonNode.DeepEquals(JsonNode.Parse(graph.ToJson()), JsonNode.Parse(json)).ShouldBeTrue();
        var node = graph.Diagram.Nodes.OfType<FlowNodeModel>().Single(item => item.NodeName == "receive");
        node.InputPortNames.ShouldBe(new[] { "Input", "ACK", "NACK" });
        foreach (var name in new[] { "ACK", "NACK", "Input" })
            node.Ports.OfType<FlowPortModel>().Where(port => port.IsInput && port.Name == name)
                .ShouldContain(port => port.Links.Count > 0);
        node.Configuration["RequeueOnNack"] = JsonSerializer.SerializeToElement(false);
        graph.RecordEdit();
        var after = graph.ToProjectJson();
        graph.Undo(); graph.ToProjectJson().ShouldBe(before);
        graph.Redo(); graph.ToProjectJson().ShouldBe(after);
        var reopened = StudioTestServices.CreateGraph(); reopened.LoadProject(after);
        reopened.ToProjectJson().ShouldBe(after);
        var saved = JsonNode.Parse(reopened.ToJson())!["Workflows"]!["main"]!;
        saved["receive"]!["ACK"]!.GetValue<string>().ShouldBe("success.Output");
        saved["receive"]!["NACK"]!.GetValue<string>().ShouldBe("failure.Output");
        saved["receive"]!["Input"]!.GetValue<string>().ShouldBe("legacy.Output");
        saved["receive"]!["Extra"]!.GetValue<int>().ShouldBe(42);
        saved["settle"]!["Consumer"]!.GetValue<string>().ShouldBe("receive");
    }
}
