using FluxFlow.Components.Routing.Contracts;
using FluxFlow.Components.Routing.Diagnostics;
using FluxFlow.Components.Routing.Nodes;
using FluxFlow.Components.Routing.Options;
using FluxFlow.Nodes;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Components.Routing.Tests;

// Join is the one two-input routing node, built directly on kit primitives. Every test
// news the node and sends FlowMessage envelopes to Left/Right; the left correlation id
// flows onto a matched result, the timed-out message's id onto its timeout.
public sealed class FlowJoinNodeTests
{
    private sealed record LeftMessage(string Key, string Payload);

    private sealed record RightMessage(string Key, string Payload);

    [Fact]
    public async Task Join_MatchesLeftAndRightByKey_CarryingLeftCorrelation()
    {
        await using var node = CreateNode();
        var output = RoutingTestSink.Link(node.Output);
        node.Timeouts.LinkTo(DataflowBlock.NullTarget<FlowMessage<FlowJoinTimeout<LeftMessage, RightMessage>>>());

        var left = FlowMessage.Create(new LeftMessage("A-100", "left"));
        await node.Left.SendAsync(left);
        await node.Right.SendAsync(FlowMessage.Create(new RightMessage("A-100", "right")));
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var result = (await RoutingTestSink.DrainUntilCompletedAsync(output)).ShouldHaveSingleItem();
        result.Payload.Key.ShouldBe("A-100");
        result.Payload.Left.Payload.ShouldBe("left");
        result.Payload.Right.Payload.ShouldBe("right");
        result.Payload.Elapsed.ShouldBeGreaterThanOrEqualTo(TimeSpan.Zero);
        result.CorrelationId.ShouldBe(left.CorrelationId);
    }

    [Fact]
    public async Task Join_MatchesOutOfOrderRightAndLeft()
    {
        await using var node = CreateNode();
        var output = RoutingTestSink.Link(node.Output);
        node.Timeouts.LinkTo(DataflowBlock.NullTarget<FlowMessage<FlowJoinTimeout<LeftMessage, RightMessage>>>());

        await node.Right.SendAsync(FlowMessage.Create(new RightMessage("A-100", "right")));
        await node.Left.SendAsync(FlowMessage.Create(new LeftMessage("A-100", "left")));
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var result = (await RoutingTestSink.DrainUntilCompletedAsync(output)).ShouldHaveSingleItem();
        result.Payload.Left.Payload.ShouldBe("left");
        result.Payload.Right.Payload.ShouldBe("right");
    }

    [Fact]
    public async Task Join_PairsDuplicateKeysInOrder()
    {
        await using var node = CreateNode();
        var output = RoutingTestSink.Link(node.Output);
        node.Timeouts.LinkTo(DataflowBlock.NullTarget<FlowMessage<FlowJoinTimeout<LeftMessage, RightMessage>>>());

        await node.Left.SendAsync(FlowMessage.Create(new LeftMessage("A-100", "left-1")));
        await node.Left.SendAsync(FlowMessage.Create(new LeftMessage("A-100", "left-2")));
        await node.Right.SendAsync(FlowMessage.Create(new RightMessage("A-100", "right-1")));
        await node.Right.SendAsync(FlowMessage.Create(new RightMessage("A-100", "right-2")));
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var results = await RoutingTestSink.DrainUntilCompletedAsync(output);
        results.Count.ShouldBe(2);
        results[0].Payload.Left.Payload.ShouldBe("left-1");
        results[0].Payload.Right.Payload.ShouldBe("right-1");
        results[1].Payload.Left.Payload.ShouldBe("left-2");
        results[1].Payload.Right.Payload.ShouldBe("right-2");
    }

    [Fact]
    public async Task Join_EmitsTimeoutWhenTimerExpires_CarryingCorrelation()
    {
        var startedAt = DateTimeOffset.Parse("2026-01-01T00:00:03Z");
        var clock = new TrackingFakeTimeProvider(startedAt);
        await using var node = CreateNode(o => o with { TimeoutMilliseconds = 25 }, clock);
        node.Output.LinkTo(DataflowBlock.NullTarget<FlowMessage<FlowJoinResult<LeftMessage, RightMessage>>>());
        var timeouts = RoutingTestSink.Link(node.Timeouts);

        var timerScheduled = clock.NextTimerScheduled;
        var left = FlowMessage.Create(new LeftMessage("A-100", "left"));
        await node.Left.SendAsync(left);
        await timerScheduled.WaitAsync(TimeSpan.FromSeconds(30));
        timeouts.TryReceive(out _).ShouldBeFalse();

        clock.Advance(TimeSpan.FromMilliseconds(25));
        var timeout = await timeouts.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        timeout.Payload.Key.ShouldBe("A-100");
        timeout.Payload.Side.ShouldBe(FlowJoinSide.Left);
        timeout.Payload.Left!.Payload.ShouldBe("left");
        timeout.Payload.Right.ShouldBeNull();
        timeout.Payload.Timeout.ShouldBe(TimeSpan.FromMilliseconds(25));
        timeout.Payload.ReceivedAt.ShouldBe(startedAt);
        timeout.Payload.TimedOutAt.ShouldBe(startedAt.AddMilliseconds(25));
        timeout.CorrelationId.ShouldBe(left.CorrelationId);
    }

    [Fact]
    public async Task Join_EmitsTimeoutsForRemainingInputsOnCompletion()
    {
        await using var node = CreateNode(o => o with { TimeoutMilliseconds = 5_000 });
        node.Output.LinkTo(DataflowBlock.NullTarget<FlowMessage<FlowJoinResult<LeftMessage, RightMessage>>>());
        var timeouts = RoutingTestSink.Link(node.Timeouts);

        await node.Right.SendAsync(FlowMessage.Create(new RightMessage("A-100", "right")));
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var timeout = (await RoutingTestSink.DrainUntilCompletedAsync(timeouts)).ShouldHaveSingleItem();
        timeout.Payload.Key.ShouldBe("A-100");
        timeout.Payload.Side.ShouldBe(FlowJoinSide.Right);
        timeout.Payload.Right!.Payload.ShouldBe("right");
    }

    [Fact]
    public async Task Join_ReportsKeyFailureAndContinues()
    {
        await using var node = new FlowJoinNode<LeftMessage, RightMessage>(
            new JoinRoutingOptions { ExpressionName = "join-v1" },
            left => left.Payload == "throw"
                ? throw new InvalidOperationException("key failed")
                : left.Key,
            right => right.Key);
        var errors = RoutingTestSink.Link(node.Errors);
        var output = RoutingTestSink.Link(node.Output);
        node.Timeouts.LinkTo(DataflowBlock.NullTarget<FlowMessage<FlowJoinTimeout<LeftMessage, RightMessage>>>());

        await node.Left.SendAsync(FlowMessage.Create(new LeftMessage("A-100", "throw")));
        await node.Left.SendAsync(FlowMessage.Create(new LeftMessage("A-101", "left")));
        await node.Right.SendAsync(FlowMessage.Create(new RightMessage("A-101", "right")));
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var error = (await RoutingTestSink.DrainUntilCompletedAsync(errors)).First();
        error.Code.ShouldBe(RoutingErrorCodes.JoinLeftKeyFailed);
        error.Context!.ShouldContain("expressionName=join-v1");
        (await RoutingTestSink.DrainUntilCompletedAsync(output)).ShouldHaveSingleItem()
            .Payload.Key.ShouldBe("A-101");
    }

    [Fact]
    public async Task Join_ReportsProcessingFailureAndContinues()
    {
        // The clock faults on its first read, so the first message the join processes fails
        // (JoinFailed). Send it alone and await the error so the one-shot fault is consumed
        // deterministically; a later pair then matches, proving the node kept processing.
        var clock = new ThrowingTimeProvider();
        await using var node = new FlowJoinNode<LeftMessage, RightMessage>(
            new JoinRoutingOptions(),
            left => left.Key,
            right => right.Key,
            clock: clock);
        var errors = RoutingTestSink.Link(node.Errors);
        var output = RoutingTestSink.Link(node.Output);
        node.Timeouts.LinkTo(DataflowBlock.NullTarget<FlowMessage<FlowJoinTimeout<LeftMessage, RightMessage>>>());

        await node.Left.SendAsync(FlowMessage.Create(new LeftMessage("A-100", "boom")));
        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        error.Code.ShouldBe(RoutingErrorCodes.JoinFailed);

        await node.Left.SendAsync(FlowMessage.Create(new LeftMessage("A-101", "left")));
        await node.Right.SendAsync(FlowMessage.Create(new RightMessage("A-101", "right")));
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        (await RoutingTestSink.DrainUntilCompletedAsync(output)).First().Payload.Key.ShouldBe("A-101");
        node.Completion.IsFaulted.ShouldBeFalse();
    }

    [Fact]
    public async Task Join_ReportsCapacityLimitAndContinues()
    {
        await using var node = CreateNode(o => o with { MaxPending = 1 });
        var errors = RoutingTestSink.Link(node.Errors);
        var output = RoutingTestSink.Link(node.Output);
        node.Timeouts.LinkTo(DataflowBlock.NullTarget<FlowMessage<FlowJoinTimeout<LeftMessage, RightMessage>>>());
        var errorTask = errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        var outputTask = output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));

        await node.Left.SendAsync(FlowMessage.Create(new LeftMessage("A-100", "left-1")));
        await node.Left.SendAsync(FlowMessage.Create(new LeftMessage("A-101", "left-2")));

        var error = await errorTask;
        error.Code.ShouldBe(RoutingErrorCodes.JoinCapacityExceeded);
        error.Context!.ShouldContain("key=A-101");

        await node.Right.SendAsync(FlowMessage.Create(new RightMessage("A-100", "right-1")));
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        (await outputTask).Payload.Key.ShouldBe("A-100");
    }

    [Fact]
    public async Task Join_EmitsMatchedEvent()
    {
        await using var node = CreateNode(o => o with { ExpressionId = "join-v1" });
        var events = RoutingTestSink.Link(node.Events);
        node.Output.LinkTo(DataflowBlock.NullTarget<FlowMessage<FlowJoinResult<LeftMessage, RightMessage>>>());
        node.Timeouts.LinkTo(DataflowBlock.NullTarget<FlowMessage<FlowJoinTimeout<LeftMessage, RightMessage>>>());

        await node.Left.SendAsync(FlowMessage.Create(new LeftMessage("A-100", "left")));
        await node.Right.SendAsync(FlowMessage.Create(new RightMessage("A-100", "right")));
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var matchedEvent = (await RoutingTestSink.DrainUntilCompletedAsync(events))
            .First(e => e.Name == RoutingDiagnosticNames.JoinMatched);
        matchedEvent.Attributes["key"].ShouldBe("A-100");
        matchedEvent.Attributes["expressionId"].ShouldBe("join-v1");
    }

    [Fact]
    public async Task Join_OutputFansOutToManyConsumers()
    {
        await using var node = CreateNode();
        var logger = RoutingTestSink.Link(node.Output);
        var mapper = RoutingTestSink.Link(node.Output);
        node.Timeouts.LinkTo(DataflowBlock.NullTarget<FlowMessage<FlowJoinTimeout<LeftMessage, RightMessage>>>());

        await node.Left.SendAsync(FlowMessage.Create(new LeftMessage("A-100", "left")));
        await node.Right.SendAsync(FlowMessage.Create(new RightMessage("A-100", "right")));
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        (await RoutingTestSink.DrainUntilCompletedAsync(logger)).ShouldHaveSingleItem()
            .Payload.Key.ShouldBe("A-100");
        (await RoutingTestSink.DrainUntilCompletedAsync(mapper)).ShouldHaveSingleItem()
            .Payload.Key.ShouldBe("A-100");
    }

    [Fact]
    public async Task Join_DisposeAfterFaultDoesNotThrow()
    {
        var node = CreateNode();

        node.Fault(new InvalidOperationException("boom"));
        await node.DisposeAsync();

        node.Completion.IsFaulted.ShouldBeTrue();
    }

    [Fact]
    public async Task Join_FaultCompletesErrorsAndFaultsOutput()
    {
        // The kit fault rule: data outputs (Output + Timeouts) fault, but Errors/Events are
        // completed (flushed) rather than faulted so buffered diagnostics survive.
        await using var node = new FlowJoinNode<LeftMessage, RightMessage>(
            new JoinRoutingOptions(),
            left => left.Key,
            right => right.Key);

        node.Fault(new InvalidOperationException("boom"));

        node.Completion.IsFaulted.ShouldBeTrue();
        // Errors/Events are completed (flushed), not faulted.
        await node.Errors.Completion.WaitAsync(TimeSpan.FromSeconds(30));
        await node.Events.Completion.WaitAsync(TimeSpan.FromSeconds(30));
        node.Errors.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        node.Events.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        // Data outputs are faulted.
        node.Output.Completion.IsFaulted.ShouldBeTrue();
        node.Timeouts.Completion.IsFaulted.ShouldBeTrue();
    }

    [Fact]
    public void Join_RejectsInvalidCapacity()
        => Should.Throw<ArgumentOutOfRangeException>(
            () => CreateNode(o => o with { BoundedCapacity = 0 }));

    [Fact]
    public void Join_RejectsInvalidTimeout()
        => Should.Throw<ArgumentOutOfRangeException>(
            () => CreateNode(o => o with { TimeoutMilliseconds = 0 }));

    [Fact]
    public void Join_RejectsNullOptions()
        => Should.Throw<ArgumentNullException>(
            () => new FlowJoinNode<LeftMessage, RightMessage>(null!, l => l.Key, r => r.Key));

    [Fact]
    public void Join_RejectsNullLeftSelector()
        => Should.Throw<ArgumentNullException>(
            () => new FlowJoinNode<LeftMessage, RightMessage>(
                new JoinRoutingOptions(), null!, r => r.Key));

    private static FlowJoinNode<LeftMessage, RightMessage> CreateNode(
        Func<JoinRoutingOptions, JoinRoutingOptions>? configure = null,
        TimeProvider? clock = null)
    {
        var options = configure?.Invoke(new JoinRoutingOptions()) ?? new JoinRoutingOptions();
        return new FlowJoinNode<LeftMessage, RightMessage>(
            options,
            left => left.Key,
            right => right.Key,
            clock: clock);
    }
}
