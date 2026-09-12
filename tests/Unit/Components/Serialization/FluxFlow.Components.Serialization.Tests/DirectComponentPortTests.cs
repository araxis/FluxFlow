using System.Threading.Tasks.Dataflow;
using FluxFlow.Core;
using FluxFlow.Components.Serialization.Nodes;
using FluxFlow.Data;
using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Serialization.Tests;

public sealed class DirectComponentPortTests
{
    [Fact]
    public async Task Direct_serialization_graph_round_trips_all_converters_and_forwards_errors()
    {
        await using var app = new FlowApplication(linkCapacity: 1);
        var workflow = app.AddWorkflow("conversion");
        var encode = new TextEncodeNode();
        var toBase64 = new Base64EncodeNode();
        var fromBase64 = new Base64DecodeNode();
        var parse = new JsonParseNode();
        var stringify = new JsonStringifyNode();
        var decode = new TextDecodeNode();
        AssertDirectPorts(encode, encode.Input, encode.Output, encode.Events);
        AssertDirectPorts(toBase64, toBase64.Input, toBase64.Output, toBase64.Events);
        AssertDirectPorts(fromBase64, fromBase64.Input, fromBase64.Output, fromBase64.Events);
        AssertDirectPorts(parse, parse.Input, parse.Output, parse.Events);
        AssertDirectPorts(stringify, stringify.Input, stringify.Output, stringify.Events);
        AssertDirectPorts(decode, decode.Input, decode.Output, decode.Events);
        var first = workflow.Add("encode", encode);
        var second = workflow.Add("toBase64", toBase64);
        var third = workflow.Add("fromBase64", fromBase64);
        var fourth = workflow.Add("parse", parse);
        var fifth = workflow.Add("stringify", stringify);
        var sixth = workflow.Add("decode", decode);
        var sink = new DirectSink();
        var serialized = new DirectSink();
        first.Instance.ShouldBeSameAs(encode);
        first.Output.LinkTo(second.Input);
        second.Output.LinkTo(third.Input);
        third.Output.LinkTo(fourth.Input);
        fourth.Output.LinkTo(fifth.Input);
        fifth.Output.LinkTo(sixth.Input);
        fifth.Output.LinkTo(workflow.Add("serialized", serialized).Input);
        sixth.Output.LinkTo(workflow.Add("sink", sink).Input);
        var original = FlowMessage.Create(FlowValue.From("{\"count\":2,\"enabled\":true}"),
            correlationId: new CorrelationId("conversion"), traceId: new TraceId("conversion-trace"));
        var error = FlowMessage.CreateError(new FlowError("upstream.failed", "Expected upstream error.", "upstream"));
        await app.StartAsync();

        (await app.SendAsync(first.Input, error)).ShouldBeTrue();
        (await sink.ReceiveAsync()).ShouldBeSameAs(error);
        (await serialized.ReceiveAsync()).ShouldBeSameAs(error);
        (await app.SendAsync(first.Input, original)).ShouldBeTrue();
        var result = await sink.ReceiveAsync();
        var serializedResult = await serialized.ReceiveAsync();
        await app.StopAsync().WaitAsync(TimeSpan.FromSeconds(10));

        result.IsError.ShouldBeFalse();
        result.Value.Deserialize<string>().ShouldBe("{\"count\":2,\"enabled\":true}");
        serializedResult.IsError.ShouldBeFalse();
        serializedResult.Value.Deserialize<FlowContent>()!.Bytes.ToArray()
            .ShouldBe(System.Text.Encoding.UTF8.GetBytes("{\"count\":2,\"enabled\":true}"));
        result.TraceId.ShouldBe(original.TraceId);
        result.CorrelationId.ShouldBe(original.CorrelationId);
        result.MessageId.ShouldNotBe(original.MessageId);
        result.CausationId.ShouldBe(serializedResult.MessageId);
        app.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        foreach (var component in new IFlowComponent[] { encode, toBase64, fromBase64, parse, stringify, decode })
            component.Completion.IsCompletedSuccessfully.ShouldBeTrue();
    }

    [Fact]
    public async Task Direct_rejected_add_leaves_component_usable()
    {
        await using var app = new FlowApplication(linkCapacity: 1);
        var workflow = app.AddWorkflow("main");
        var encode = workflow.Add("encode", new TextEncodeNode());
        await using var decode = new TextDecodeNode();

        Should.Throw<ArgumentException>(() => workflow.Add("encode", decode));
        decode.Completion.IsCompleted.ShouldBeFalse();
        var accepted = workflow.Add("decode", decode, ComponentOwnership.Borrowed);
        accepted.Instance.ShouldBeSameAs(decode);
        encode.Output.LinkTo(accepted.Input);
        var sink = new DirectSink();
        accepted.Output.LinkTo(workflow.Add("sink", sink).Input);
        await app.StartAsync();
        (await app.SendAsync(encode.Input, FlowMessage.Create(FlowValue.From("still usable")))).ShouldBeTrue();
        (await sink.ReceiveAsync()).Value.ShouldNotBeNull().Deserialize<string>().ShouldBe("still usable");
        await app.StopAsync().WaitAsync(TimeSpan.FromSeconds(10));
        app.Completion.IsCompletedSuccessfully.ShouldBeTrue();
    }

    private static void AssertDirectPorts(IFlowComponent component, ITargetBlock<FlowMessage> input,
        ISourceBlock<FlowMessage> output, ISourceBlock<FlowMessage> events)
    {
        component.Ports.ShouldBeSameAs(component.Ports);
        component.Ports.Inputs.ShouldHaveSingleItem().Key.ShouldBe("Input");
        component.Ports.Outputs.ShouldHaveSingleItem().Key.ShouldBe("Output");
        component.Ports.Inputs["Input"].ShouldBeSameAs(input);
        component.Ports.Outputs["Output"].ShouldBeSameAs(output);
        component.Ports.Events.ShouldBeSameAs(events);
        component.Ports.Controls.ShouldBeEmpty();
    }

    private sealed class DirectSink : IFlowComponent
    {
        private readonly BufferBlock<FlowMessage> _input = new(
            new DataflowBlockOptions { BoundedCapacity = 2 });

        public DirectSink() => Ports = new FlowPorts(
            inputs: new Dictionary<string, ITargetBlock<FlowMessage>> { ["Input"] = _input });

        public FlowPorts Ports { get; }
        public Task Completion => _input.Completion;
        public Task<FlowMessage> ReceiveAsync() => _input.ReceiveAsync()
            .WaitAsync(TimeSpan.FromSeconds(10));
        public void Complete() => _input.Complete();
        public void Fault(Exception exception) => ((IDataflowBlock)_input).Fault(exception);
        public async ValueTask DisposeAsync() { Complete(); await Completion; }
    }
}
