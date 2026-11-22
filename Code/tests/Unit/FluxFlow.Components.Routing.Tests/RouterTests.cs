using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Messaging;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Routing.Tests;

public sealed class RouterTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);
    private static FlowMessage Message(int value) => FlowMessage.Create(new JsonFlowValue(JsonSerializer.SerializeToElement(value)));

    [Fact]
    public async Task All_matching_routes_receive_exact_message_and_unmatched_has_separate_output()
    {
        Predicate<FlowMessage>[] routes = [m => m.Value.Json.GetInt32() > 0, m => m.Value.Json.GetInt32() % 2 == 0];
        await using var router = new Router(new() { Routes = routes });
        routes[0] = _ => false; // Construction snapshots the route list.
        var first = Sink(router.Outputs[0]);
        var second = Sink(router.Outputs[1]);
        var unmatched = Sink(router.Unmatched);
        var errors = Sink(router.Output);
        var both = Message(2);
        var none = Message(-1);
        await router.Input.SendAsync(both);
        await router.Input.SendAsync(none);
        router.Complete();
        (await first.ReceiveAsync(Deadline)).ShouldBeSameAs(both);
        (await second.ReceiveAsync(Deadline)).ShouldBeSameAs(both);
        (await unmatched.ReceiveAsync(Deadline)).ShouldBeSameAs(none);
        await router.Completion.WaitAsync(Deadline);
        await Task.WhenAll(first.Completion, second.Completion, unmatched.Completion).WaitAsync(Deadline);
        errors.Count.ShouldBe(0);
    }

    [Fact]
    public async Task Predicate_failure_has_no_partial_dispatch_and_next_message_continues()
    {
        await using var router = new Router(new() { Routes = [ _ => true, v =>
        {
            if (v.Value.Json.GetInt32() == 1) throw new InvalidOperationException("bad condition");
            return false;
        }] });
        var first = Sink(router.Outputs[0]);
        var unused = Sink(router.Outputs[1]);
        var unmatched = Sink(router.Unmatched);
        var errors = Sink(router.Output);
        var existing = FlowMessage.Create(new FlowError("test", "error", "test"));
        var next = Message(2);
        await router.Input.SendAsync(existing);
        await router.Input.SendAsync(Message(1));
        await router.Input.SendAsync(next);
        router.Complete();
        (await errors.ReceiveAsync(Deadline)).ShouldBeSameAs(existing);
        (await errors.ReceiveAsync(Deadline)).Error!.Code.ShouldBe("node.processing_failed");
        (await first.ReceiveAsync(Deadline)).ShouldBeSameAs(next);
        await router.Completion.WaitAsync(Deadline);
        first.Count.ShouldBe(0);
        unused.Count.ShouldBe(0);
        unmatched.Count.ShouldBe(0);
    }

    [Fact]
    public void Null_conditions_are_rejected_before_constructing_blocks()
    {
        Should.Throw<ArgumentNullException>(() => new Router(null!));
        Should.Throw<ArgumentException>(() => new Router(new() { Routes = [null!] }));
    }

    private static BufferBlock<FlowMessage> Sink(ISourceBlock<FlowMessage> source)
    {
        var sink = new BufferBlock<FlowMessage>();
        source.LinkTo(sink, new() { PropagateCompletion = true });
        return sink;
    }
}
