using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Routing.Contracts;
using FluxFlow.Components.Routing.Diagnostics;
using FluxFlow.Components.Routing.Nodes;
using FluxFlow.Components.Routing.Options;
using FluxFlow.Data;
using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Routing.Tests;

public sealed class JoinNodeRuntimeTests
{
    private sealed record LeftMessage(string Key, string Payload);
    private sealed record RightMessage(string Key, string Payload);

    [Fact]
    public async Task Join_matches_both_sides_with_left_lineage()
    {
        await using var node = CreateNode();
        var output = RoutingTestSink.Link(node.Output);
        var left = FlowMessage.Create(new LeftMessage("A-100", "left"));

        await node.Left.SendAsync(left);
        await node.Right.SendAsync(FlowMessage.Create(new RightMessage("A-100", "right")));
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var message = (await RoutingTestSink.DrainUntilCompletedAsync(output)).ShouldHaveSingleItem();
        var match = Matched(message);
        match.Key.ShouldBe("A-100");
        match.Left.Payload.ShouldBe("left");
        match.Right.Payload.ShouldBe("right");
        match.Elapsed.ShouldBeGreaterThanOrEqualTo(TimeSpan.Zero);
        message.CorrelationId.ShouldBe(left.CorrelationId);
    }

    [Fact]
    public async Task Join_matches_out_of_order_and_duplicate_keys_in_order()
    {
        await using var node = CreateNode();
        var output = RoutingTestSink.Link(node.Output);

        await node.Right.SendAsync(FlowMessage.Create(new RightMessage("A-100", "right-1")));
        await node.Left.SendAsync(FlowMessage.Create(new LeftMessage("A-100", "left-1")));
        await node.Left.SendAsync(FlowMessage.Create(new LeftMessage("A-100", "left-2")));
        await node.Right.SendAsync(FlowMessage.Create(new RightMessage("A-100", "right-2")));
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var matches = (await RoutingTestSink.DrainUntilCompletedAsync(output)).Select(Matched).ToArray();
        matches.Length.ShouldBe(2);
        matches[0].Left.Payload.ShouldBe("left-1");
        matches[0].Right.Payload.ShouldBe("right-1");
        matches[1].Left.Payload.ShouldBe("left-2");
        matches[1].Right.Payload.ShouldBe("right-2");
    }

    [Fact]
    public async Task Join_emits_timeout_with_source_lineage()
    {
        var startedAt = DateTimeOffset.Parse("2026-01-01T00:00:03Z");
        var clock = new TrackingFakeTimeProvider(startedAt);
        await using var node = CreateNode(
            options => options with { TimeoutMilliseconds = 25 },
            clock);
        var output = RoutingTestSink.Link(node.Output);
        var timerScheduled = clock.NextTimerScheduled;
        var left = FlowMessage.Create(new LeftMessage("A-100", "left"));

        await node.Left.SendAsync(left);
        await timerScheduled.WaitAsync(TimeSpan.FromSeconds(30));
        output.TryReceive(out _).ShouldBeFalse();
        clock.Advance(TimeSpan.FromMilliseconds(25));

        var message = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        var timeout = message.Value.ShouldBeOfType<FlowJoinTimedOutOutcome<LeftMessage, RightMessage>>().Timeout;
        timeout.Key.ShouldBe("A-100");
        timeout.Side.ShouldBe(FlowJoinSide.Left);
        timeout.Left!.Payload.ShouldBe("left");
        timeout.Right.ShouldBeNull();
        timeout.ReceivedAt.ShouldBe(startedAt);
        timeout.TimedOutAt.ShouldBe(startedAt.AddMilliseconds(25));
        message.CorrelationId.ShouldBe(left.CorrelationId);

        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task Join_emits_pending_timeout_on_completion()
    {
        await using var node = CreateNode();
        var output = RoutingTestSink.Link(node.Output);

        await node.Right.SendAsync(FlowMessage.Create(new RightMessage("A-100", "right")));
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var timeout = (await RoutingTestSink.DrainUntilCompletedAsync(output)).ShouldHaveSingleItem()
            .Value.ShouldBeOfType<FlowJoinTimedOutOutcome<LeftMessage, RightMessage>>().Timeout;
        timeout.Side.ShouldBe(FlowJoinSide.Right);
        timeout.Right!.Payload.ShouldBe("right");
    }

    [Fact]
    public async Task Join_reports_selector_failure_and_continues()
    {
        await using var node = new JoinNodeRuntime<LeftMessage, RightMessage>(
            new JoinRoutingOptions { ExpressionName = "join-v1" },
            left => left.Payload == "throw"
                ? throw new InvalidOperationException("key failed")
                : left.Key,
            right => right.Key);
        var output = RoutingTestSink.Link(node.Output);

        await node.Left.SendAsync(FlowMessage.Create(new LeftMessage("A-100", "throw")));
        await node.Left.SendAsync(FlowMessage.Create(new LeftMessage("A-101", "left")));
        await node.Right.SendAsync(FlowMessage.Create(new RightMessage("A-101", "right")));
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var messages = await RoutingTestSink.DrainUntilCompletedAsync(output);
        var error = messages.Single(message => message.IsError).Error.ShouldNotBeNull();
        error.Code.ShouldBe(RoutingErrorCodeNames.OperationFailed);
        error.Details!.Value.GetProperty("legacyCode").GetInt32()
            .ShouldBe(RoutingErrorCodes.JoinLeftKeyFailed);
        error.Details.Value.GetProperty("context").GetString().ShouldContain("expressionName=join-v1");
        Matched(messages.Single(message => !message.IsError)).Key.ShouldBe("A-101");
    }

    [Fact]
    public async Task Join_reports_capacity_and_keeps_processing()
    {
        await using var node = CreateNode(options => options with { MaxPending = 1 });
        var output = RoutingTestSink.Link(node.Output);

        await node.Left.SendAsync(FlowMessage.Create(new LeftMessage("A-100", "left-1")));
        await node.Left.SendAsync(FlowMessage.Create(new LeftMessage("A-101", "left-2")));
        await node.Right.SendAsync(FlowMessage.Create(new RightMessage("A-100", "right-1")));
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var messages = await RoutingTestSink.DrainUntilCompletedAsync(output);
        var error = messages.Single(message => message.IsError).Error.ShouldNotBeNull();
        error.Details!.Value.GetProperty("legacyCode").GetInt32()
            .ShouldBe(RoutingErrorCodes.JoinCapacityExceeded);
        Matched(messages.Single(message => !message.IsError)).Key.ShouldBe("A-100");
    }

    [Fact]
    public async Task Join_emits_match_diagnostic_and_fans_out()
    {
        await using var node = CreateNode(options => options with { ExpressionId = "join-v1" });
        var events = RoutingTestSink.Link(node.Events);
        var first = RoutingTestSink.Link(node.Output);
        var second = RoutingTestSink.Link(node.Output);

        await node.Left.SendAsync(FlowMessage.Create(new LeftMessage("A-100", "left")));
        await node.Right.SendAsync(FlowMessage.Create(new RightMessage("A-100", "right")));
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        Matched((await RoutingTestSink.DrainUntilCompletedAsync(first)).ShouldHaveSingleItem())
            .Key.ShouldBe("A-100");
        Matched((await RoutingTestSink.DrainUntilCompletedAsync(second)).ShouldHaveSingleItem())
            .Key.ShouldBe("A-100");
        var matched = (await RoutingTestSink.DrainUntilCompletedAsync(events))
            .Single(@event => @event.Name == RoutingDiagnosticNames.JoinMatched);
        matched.Attributes["expressionId"].ShouldBe("join-v1");
    }

    [Fact]
    public async Task Join_propagates_incoming_error()
    {
        await using var node = CreateNode();
        var output = RoutingTestSink.Link(node.Output);
        var input = FlowMessage.CreateError<LeftMessage>(
            new FlowError("input.failed", "Input failed.", "test"));

        await node.Left.SendAsync(input);
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var propagated = (await RoutingTestSink.DrainUntilCompletedAsync(output)).ShouldHaveSingleItem();
        propagated.IsError.ShouldBeTrue();
        propagated.Error!.Code.ShouldBe("input.failed");
        propagated.TraceId.ShouldBe(input.TraceId);
    }

    [Fact]
    public async Task Join_faults_unified_output_but_completes_events()
    {
        await using var node = CreateNode();
        node.Fault(new InvalidOperationException("boom"));

        node.Completion.IsFaulted.ShouldBeTrue();
        await node.Events.Completion.WaitAsync(TimeSpan.FromSeconds(30));
        node.Events.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        await Should.ThrowAsync<Exception>(async () =>
            await node.Output.Completion.WaitAsync(TimeSpan.FromSeconds(30)));
        node.Output.Completion.IsFaulted.ShouldBeTrue();
    }

    [Fact]
    public void Join_rejects_invalid_configuration()
    {
        Should.Throw<ArgumentOutOfRangeException>(
            () => CreateNode(options => options with { BoundedCapacity = 0 }));
        Should.Throw<ArgumentOutOfRangeException>(
            () => CreateNode(options => options with { TimeoutMilliseconds = 0 }));
        Should.Throw<ArgumentNullException>(
            () => new JoinNodeRuntime<LeftMessage, RightMessage>(
                null!, left => left.Key, right => right.Key));
        Should.Throw<ArgumentNullException>(
            () => new JoinNodeRuntime<LeftMessage, RightMessage>(
                new JoinRoutingOptions(), null!, right => right.Key));
    }

    private static FlowJoinResult<LeftMessage, RightMessage> Matched(
        FlowMessage<FlowJoinOutcome<LeftMessage, RightMessage>> message)
        => message.Value.ShouldBeOfType<FlowJoinMatchedOutcome<LeftMessage, RightMessage>>().Match;

    private static JoinNodeRuntime<LeftMessage, RightMessage> CreateNode(
        Func<JoinRoutingOptions, JoinRoutingOptions>? configure = null,
        TimeProvider? clock = null)
    {
        var options = configure?.Invoke(new JoinRoutingOptions()) ?? new JoinRoutingOptions();
        return new JoinNodeRuntime<LeftMessage, RightMessage>(
            options,
            left => left.Key,
            right => right.Key,
            clock: clock);
    }
}
