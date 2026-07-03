using FluxFlow.Components.Routing.Diagnostics;
using FluxFlow.Components.Routing.Nodes;
using FluxFlow.Components.Routing.Options;
using FluxFlow.Nodes;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Components.Routing.Tests;

// Merge is a fan-in node: several upstreams of the same type all link into the single
// bounded Input (a BufferBlock already merges concurrent producers), and the node
// re-broadcasts each message on Output preserving the correlation id.
public sealed class FlowMergeNodeTests
{
    [Fact]
    public async Task Merge_ReEmitsEveryInput_PreservingCorrelation()
    {
        await using var node = new FlowMergeNode<string>(new MergeRoutingOptions());
        var output = RoutingTestSink.Link(node.Output);

        var one = FlowMessage.Create("one");
        var two = FlowMessage.Create("two");
        await node.Input.SendAsync(one);
        await node.Input.SendAsync(two);
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var values = await RoutingTestSink.DrainUntilCompletedAsync(output);
        values.Select(m => m.Payload).ShouldBe(["one", "two"]);
        values[0].CorrelationId.ShouldBe(one.CorrelationId);
        values[1].CorrelationId.ShouldBe(two.CorrelationId);
    }

    [Fact]
    public async Task Merge_FansInFromMultipleUpstreams()
    {
        // Two independent upstream sources both link into the single merge Input; the
        // BufferBlock merges them. Order between sources is not guaranteed, so assert on
        // the set of payloads, not their interleaving.
        await using var node = new FlowMergeNode<int>(new MergeRoutingOptions());
        var output = RoutingTestSink.Link(node.Output);

        var left = new BufferBlock<FlowMessage<int>>();
        var right = new BufferBlock<FlowMessage<int>>();
        left.LinkTo(node.Input, new DataflowLinkOptions { PropagateCompletion = false });
        right.LinkTo(node.Input, new DataflowLinkOptions { PropagateCompletion = false });

        await left.SendAsync(FlowMessage.Create(1));
        await right.SendAsync(FlowMessage.Create(2));
        // Once both upstreams complete, complete the merge input.
        left.Complete();
        right.Complete();
        await Task.WhenAll(left.Completion, right.Completion);
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        (await RoutingTestSink.DrainUntilCompletedAsync(output))
            .Select(m => m.Payload)
            .ShouldBe([1, 2], ignoreOrder: true);
    }

    [Fact]
    public async Task Merge_EmitsEventWithSequence()
    {
        await using var node = new FlowMergeNode<int>(new MergeRoutingOptions());
        var output = RoutingTestSink.Link(node.Output);
        var events = RoutingTestSink.Link(node.Events);

        await node.Input.SendAsync(FlowMessage.Create(1));
        await node.Input.SendAsync(FlowMessage.Create(2));
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        (await RoutingTestSink.DrainUntilCompletedAsync(output)).Count.ShouldBe(2);
        var emitted = await RoutingTestSink.DrainUntilCompletedAsync(events);
        emitted.Count.ShouldBe(2);
        emitted[0].Name.ShouldBe(RoutingDiagnosticNames.MergeEmitted);
        emitted.Select(e => e.Attributes["sequence"]).ShouldBe([1L, 2L]);
    }

    [Fact]
    public async Task Merge_UsesConfiguredClockForEventTimestamp()
    {
        var timestamp = DateTimeOffset.Parse("2026-01-01T00:00:01Z");
        await using var node = new FlowMergeNode<string>(
            new MergeRoutingOptions(),
            new FakeTimeProvider(timestamp));
        var events = RoutingTestSink.Link(node.Events);
        node.Output.LinkTo(DataflowBlock.NullTarget<FlowMessage<string>>());

        await node.Input.SendAsync(FlowMessage.Create("value"));
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        (await RoutingTestSink.DrainUntilCompletedAsync(events)).ShouldHaveSingleItem()
            .Timestamp.ShouldBe(timestamp);
    }

    [Fact]
    public void Merge_RejectsInvalidCapacity()
        => Should.Throw<ArgumentOutOfRangeException>(
            () => new FlowMergeNode<int>(new MergeRoutingOptions { BoundedCapacity = 0 }));

    [Fact]
    public void Merge_RejectsBlankInputType()
        => Should.Throw<ArgumentException>(
            () => new FlowMergeNode<int>(new MergeRoutingOptions { InputType = " " }))
            .Message.ShouldContain("inputType");

    [Fact]
    public void Merge_RejectsNullOptions()
        => Should.Throw<ArgumentNullException>(() => new FlowMergeNode<int>(null!));
}
