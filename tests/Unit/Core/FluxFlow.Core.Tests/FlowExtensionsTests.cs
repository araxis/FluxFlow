using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Data;
using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Core.Tests;

public sealed class FlowExtensionsTests
{
    [Fact]
    public async Task Direct_extensions_route_real_messages_with_captured_predicates_and_cross_workflow_ports()
    {
        await using var app = new FlowApplication();
        var archiveNode = new Sink();
        var selectedNode = new Sink();
        var archive = app.AddWorkflow("archive").Add("sink", archiveNode);
        var threshold = 5;
        FlowComponent source = null!;
        app.Workflow("main", workflow =>
        {
            source = workflow.Add("source", new Echo());
            var selected = workflow.Add("selected", selectedNode);
            source.Output.To(archive.Input)
                .To(selected.Input, when: message => message.Value!.Json.Deserialize<int>(new JsonSerializerOptions(JsonSerializerDefaults.Web)) > threshold)
                .ShouldBeSameAs(source.Output);
        }).ShouldBeSameAs(app);

        await app.StartAsync();
        await source.Input.Block.SendAsync(FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(3, new JsonSerializerOptions(JsonSerializerDefaults.Web)))));
        await source.Input.Block.SendAsync(FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(7, new JsonSerializerOptions(JsonSerializerDefaults.Web)))));
        await app.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
        archiveNode.Values.ShouldBe([3, 7]);
        selectedNode.Values.ShouldBe([7]);
    }

    [Fact]
    public async Task Input_chaining_keeps_fan_in_and_application_ownership()
    {
        await using var app = new FlowApplication();
        var workflow = app.AddWorkflow("main");
        var first = workflow.Add("first", new Echo());
        var second = workflow.Add("second", new Echo());
        var sinkNode = new Sink();
        var sink = workflow.Add("sink", sinkNode);
        sink.Input.From(first.Output).From(second.Output, message => message.Value!.Json.Deserialize<int>(new JsonSerializerOptions(JsonSerializerDefaults.Web)) > 0)
            .ShouldBeSameAs(sink.Input);
        second.Output.DiscardUnmatched();
        await app.StartAsync();
        await first.Input.Block.SendAsync(FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(1, new JsonSerializerOptions(JsonSerializerDefaults.Web)))));
        await second.Input.Block.SendAsync(FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(-1, new JsonSerializerOptions(JsonSerializerDefaults.Web)))));
        await second.Input.Block.SendAsync(FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(2, new JsonSerializerOptions(JsonSerializerDefaults.Web)))));
        await app.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
        sinkNode.Values.Order().ShouldBe([1, 2]);
        Should.Throw<InvalidOperationException>(() => first.Output.To(sink.Input));
    }

    private sealed class Echo : FlowTransformNode
    {
        protected override Task<FlowMessage> TransformAsync(FlowMessage message) => Task.FromResult(message);
    }

    private sealed class Sink : FlowNode
    {
        public List<int> Values { get; } = [];
        protected override Task ProcessAsync(FlowMessage message)
        {
            Values.Add(message.Value!.Json.Deserialize<int>(new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            return Task.CompletedTask;
        }
    }
}
