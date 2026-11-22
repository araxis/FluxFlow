using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Messaging;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Source.Tests;

public sealed class SourceTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Each_trigger_emits_next_snapshot_value_with_trigger_lineage()
    {
        var number = new JsonFlowValue(JsonSerializer.SerializeToElement(12));
        var text = new JsonFlowValue(JsonSerializer.SerializeToElement("test"));
        FlowValue[] values = [number, text, JsonFlowValue.Null];
        await using var source = new Source(new() { Values = values, OutputCapacity = 1 });
        values[0] = text;
        var sink = Sink(source);
        sink.Count.ShouldBe(0);
        foreach (var expected in new FlowValue[] { number, text, JsonFlowValue.Null })
        {
            var trigger = FlowMessage.Create(JsonFlowValue.Null, correlationId: CorrelationId.New(),
                headers: new Dictionary<string, FlowValue> { ["test"] = "kept" });
            await source.Input.SendAsync(trigger);
            var message = await sink.ReceiveAsync(Deadline);
            message.Value.ShouldBeSameAs(expected);
            message.MessageId.ShouldNotBe(trigger.MessageId);
            message.CausationId.ShouldBe(trigger.MessageId);
            message.TraceId.ShouldBe(trigger.TraceId);
            message.CorrelationId.ShouldBe(trigger.CorrelationId);
            message.Headers["test"].ShouldBeSameAs(trigger.Headers["test"]);
        }
        await source.Input.SendAsync(FlowMessage.Create(JsonFlowValue.Null));
        source.Complete();
        await source.Completion.WaitAsync(Deadline);
        sink.Count.ShouldBe(0);
        await sink.Completion.WaitAsync(Deadline);
    }

    [Fact]
    public async Task Error_trigger_is_forwarded_without_consuming_value()
    {
        await using var source = new Source(new() { Values = [JsonFlowValue.Null] });
        var sink = Sink(source);
        var error = FlowMessage.Create(new FlowError("test", "failure", "test"));
        await source.Input.SendAsync(error);
        await source.Input.SendAsync(FlowMessage.Create(JsonFlowValue.Null));
        source.Complete();
        (await sink.ReceiveAsync(Deadline)).ShouldBeSameAs(error);
        (await sink.ReceiveAsync(Deadline)).IsError.ShouldBeFalse();
        await source.Completion.WaitAsync(Deadline);
        await sink.Completion.WaitAsync(Deadline);
    }

    [Fact]
    public async Task Empty_list_accepts_triggers_without_output()
    {
        await using var source = new Source(new() { Values = [] });
        var sink = Sink(source);
        await source.Input.SendAsync(FlowMessage.Create(JsonFlowValue.Null));
        source.Complete();
        await source.Completion.WaitAsync(Deadline);
        sink.Count.ShouldBe(0);
    }

    [Fact]
    public void Invalid_values_are_rejected_at_construction()
    {
        Should.Throw<ArgumentNullException>(() => new Source(null!));
        Should.Throw<ArgumentException>(() => new Source(new() { Values = [null!] }));
        Should.Throw<ArgumentOutOfRangeException>(() => new Source(new() { Values = [], OutputCapacity = 0 }));
    }

    private static BufferBlock<FlowMessage> Sink(Source source)
    {
        var sink = new BufferBlock<FlowMessage>();
        source.Output.LinkTo(sink, new() { PropagateCompletion = true });
        return sink;
    }
}
