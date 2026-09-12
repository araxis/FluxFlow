using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Routing.Contracts;
using FluxFlow.Components.Routing.Diagnostics;
using FluxFlow.Components.Routing.Nodes;
using FluxFlow.Components.Routing.Options;
using FluxFlow.Core;
using FluxFlow.Data;
using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Routing.Tests;

public sealed class JoinNodeTests
{
    private sealed record LeftMessage(string Key, string Payload);
    private sealed record RightMessage(string Key, string Payload);

    [Fact]
    public async Task Join_matches_both_sides_with_left_lineage()
    {
        await using var node = CreateNode();
        var output = RoutingTestSink.Link(node.Output);
        var left = Message(new LeftMessage("A-100", "left"));

        await node.Left.SendAsync(left);
        await node.Right.SendAsync(Message(new RightMessage("A-100", "right")));
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

        await node.Right.SendAsync(Message(new RightMessage("A-100", "right-1")));
        await node.Left.SendAsync(Message(new LeftMessage("A-100", "left-1")));
        await node.Left.SendAsync(Message(new LeftMessage("A-100", "left-2")));
        await node.Right.SendAsync(Message(new RightMessage("A-100", "right-2")));
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
        var left = Message(new LeftMessage("A-100", "left"));

        await node.Left.SendAsync(left);
        await timerScheduled.WaitAsync(TimeSpan.FromSeconds(30));
        output.TryReceive(out _).ShouldBeFalse();
        clock.Advance(TimeSpan.FromMilliseconds(25));

        var message = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        var timeout = message.Value.ShouldNotBeNull().Deserialize<FlowJoinTimedOutOutcome<LeftMessage, RightMessage>>().ShouldNotBeNull().Timeout;
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

        await node.Right.SendAsync(Message(new RightMessage("A-100", "right")));
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var timeout = (await RoutingTestSink.DrainUntilCompletedAsync(output)).ShouldHaveSingleItem()
            .Value.ShouldNotBeNull().Deserialize<FlowJoinTimedOutOutcome<LeftMessage, RightMessage>>().ShouldNotBeNull().Timeout;
        timeout.Side.ShouldBe(FlowJoinSide.Right);
        timeout.Right!.Payload.ShouldBe("right");
    }

    [Fact]
    public async Task Join_reports_selector_failure_and_continues()
    {
        await using var node = new JoinNode<LeftMessage, RightMessage>(
            new JoinRoutingOptions { ExpressionName = "join-v1" },
            left => left.Payload == "throw"
                ? throw new InvalidOperationException("key failed")
                : left.Key,
            right => right.Key);
        var output = RoutingTestSink.Link(node.Output);

        await node.Left.SendAsync(Message(new LeftMessage("A-100", "throw")));
        await node.Left.SendAsync(Message(new LeftMessage("A-101", "left")));
        await node.Right.SendAsync(Message(new RightMessage("A-101", "right")));
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var messages = await RoutingTestSink.DrainUntilCompletedAsync(output);
        var error = messages.Single(message => message.IsError).Error.ShouldNotBeNull();
        error.Code.ShouldBe(RoutingErrorCodeNames.OperationFailed);
        error.Details!.Value.GetProperty("legacyCode").GetInt32()
            .ShouldBe(RoutingErrorCodes.JoinLeftKeyFailed);
        error.Details.Value.GetProperty("context").GetString()!.ShouldContain("expressionName=join-v1");
        Matched(messages.Single(message => !message.IsError)).Key.ShouldBe("A-101");
    }

    [Fact]
    public async Task Join_reports_capacity_and_keeps_processing()
    {
        var secondLeftCommandEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using var node = new JoinNode<LeftMessage, RightMessage>(
            new JoinRoutingOptions { MaxPending = 1 },
            left =>
            {
                if (left.Key == "A-101")
                    secondLeftCommandEntered.TrySetResult();

                return left.Key;
            },
            right => right.Key);
        var output = RoutingTestSink.Link(node.Output);

        await node.Left.SendAsync(Message(new LeftMessage("A-100", "left-1")));
        await node.Left.SendAsync(Message(new LeftMessage("A-101", "left-2")));
        await secondLeftCommandEntered.Task.WaitAsync(TimeSpan.FromSeconds(30));
        await node.Right.SendAsync(Message(new RightMessage("A-100", "right-1")));
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var messages = await RoutingTestSink.DrainUntilCompletedAsync(output);
        var error = messages.Single(message => message.IsError).Error.ShouldNotBeNull();
        error.Details!.Value.GetProperty("legacyCode").GetInt32()
            .ShouldBe(RoutingErrorCodes.JoinCapacityExceeded);
        Matched(messages.Single(message => !message.IsError)).Key.ShouldBe("A-100");
    }

    [Fact]
    public async Task Join_output_drains_committed_outcomes_before_completion()
    {
        var timeout = TimeSpan.FromSeconds(30);
        await using var node = CreateNode(
            options => options with { BoundedCapacity = 1 });
        var output = new PostponedTargetBlock<
            FlowMessage>();
        using var outputLink = node.Output.LinkTo(
            output,
            new DataflowLinkOptions { PropagateCompletion = true });
        var firstLeft = Message(new LeftMessage("A-100", "left-1"));
        var secondLeft = Message(new LeftMessage("A-200", "left-2"));

        (await node.Left.SendAsync(firstLeft).WaitAsync(timeout)).ShouldBeTrue();
        (await node.Right.SendAsync(
            Message(new RightMessage("A-100", "right-1"))).WaitAsync(timeout))
            .ShouldBeTrue();
        (await node.Left.SendAsync(secondLeft).WaitAsync(timeout)).ShouldBeTrue();
        (await node.Right.SendAsync(
            Message(new RightMessage("A-200", "right-2"))).WaitAsync(timeout))
            .ShouldBeTrue();
        node.Complete();

        await output.WaitForOfferAsync(timeout);
        node.Completion.IsCompleted.ShouldBeFalse();
        output.AcceptNext();
        await output.WaitForOfferAsync(timeout);
        node.Completion.IsCompleted.ShouldBeFalse();
        output.AcceptNext();

        await node.Completion.WaitAsync(timeout);
        await output.Completion.WaitAsync(timeout);
        var messages = output.Accepted;
        messages.Select(Matched).Select(static match => match.Key)
            .ShouldBe(["A-100", "A-200"]);
        messages.Select(static message => message.CorrelationId)
            .ShouldBe([firstLeft.CorrelationId, secondLeft.CorrelationId]);
    }

    [Fact]
    public async Task Join_emits_match_diagnostic_and_fans_out()
    {
        await using var node = CreateNode(options => options with { ExpressionId = "join-v1" });
        var events = RoutingTestSink.Link(node.Events);
        var first = RoutingTestSink.Link(node.Output);
        var second = RoutingTestSink.Link(node.Output);

        await node.Left.SendAsync(Message(new LeftMessage("A-100", "left")));
        await node.Right.SendAsync(Message(new RightMessage("A-100", "right")));
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        Matched((await RoutingTestSink.DrainUntilCompletedAsync(first)).ShouldHaveSingleItem())
            .Key.ShouldBe("A-100");
        Matched((await RoutingTestSink.DrainUntilCompletedAsync(second)).ShouldHaveSingleItem())
            .Key.ShouldBe("A-100");
        var matched = (await RoutingTestSink.DrainUntilCompletedAsync(events))
            .Single(@event => @event.ToFlowEvent().Name == RoutingDiagnosticNames.JoinMatched);
        matched.ToFlowEvent().Attributes["expressionId"].ShouldBe("join-v1");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Join_propagates_incoming_error(bool fromLeft)
    {
        await using var node = CreateNode();
        var output = RoutingTestSink.Link(node.Output);
        var input = FlowMessage.CreateError(
            new FlowError("input.failed", "Input failed.", "test"));

        await (fromLeft ? node.Left : node.Right).SendAsync(input);
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var propagated = (await RoutingTestSink.DrainUntilCompletedAsync(output)).ShouldHaveSingleItem();
        propagated.IsError.ShouldBeTrue();
        propagated.Error!.Code.ShouldBe("input.failed");
        propagated.TraceId.ShouldBe(input.TraceId);
        propagated.ShouldBeSameAs(input);
    }

    [Fact]
    public async Task Join_faults_unified_output_but_completes_events()
    {
        await using var node = CreateNode();
        var failure = new InvalidOperationException("boom");
        node.Fault(failure);

        var observed = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await node.Completion.WaitAsync(TimeSpan.FromSeconds(30)));
        observed.ShouldBeSameAs(failure);
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
            () => new JoinNode<LeftMessage, RightMessage>(
                null!, left => left.Key, right => right.Key));
        Should.Throw<ArgumentNullException>(
            () => new JoinNode<LeftMessage, RightMessage>(
                new JoinRoutingOptions(), null!, right => right.Key));
    }

    [Fact]
    public async Task Join_runs_as_a_direct_component_with_two_canonical_inputs()
    {
        await using var application = new FlowApplication();
        var instance = CreateNode();
        var component = application.AddWorkflow("main").Add("join", instance);
        instance.Ports.Inputs.Keys.Order().ShouldBe(["Left", "Right"]);
        instance.Ports.Inputs["Left"].ShouldBeSameAs(instance.Left);
        instance.Ports.Inputs["Right"].ShouldBeSameAs(instance.Right);
        component.Inputs["Left"].Address.ShouldBe("main.join.Left");
        component.Inputs["Right"].Address.ShouldBe("main.join.Right");
        application.RepliesFrom(component.Output);
        await application.StartAsync();

        var pending = application.RequestAsync(component.Inputs["Left"], component.Output,
            Message(new LeftMessage("key", "left")), TimeSpan.FromSeconds(5));
        await application.SendAsync(component.Inputs["Right"], Message(new RightMessage("key", "right")));
        var match = Matched(await pending);

        match.Left.Payload.ShouldBe("left");
        match.Right.Payload.ShouldBe("right");
        await application.StopAsync();
    }

    [Fact]
    public async Task Join_reports_invalid_materialization_then_matches_valid_inputs()
    {
        await using var node = CreateNode();
        var output = RoutingTestSink.Link(node.Output);
        (await node.Left.SendAsync(Message("not an object"))).ShouldBeTrue();
        (await node.Left.SendAsync(Message(new LeftMessage("key", "left")))).ShouldBeTrue();
        (await node.Right.SendAsync(Message(new RightMessage("key", "right")))).ShouldBeTrue();
        node.Complete();

        var results = await RoutingTestSink.DrainUntilCompletedAsync(output);

        results.Count.ShouldBe(2);
        results.Single(message => message.IsError).Error!.Code.ShouldBe(RoutingErrorCodeNames.OperationFailed);
        var match = Matched(results.Single(message => !message.IsError));
        match.Left.Payload.ShouldBe("left");
        match.Right.Payload.ShouldBe("right");
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Join_bounded_input_backpressures_until_active_processing_advances()
    {
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var node = new JoinNode<LeftMessage, RightMessage>(
            new JoinRoutingOptions { BoundedCapacity = 1 }, left =>
            {
                if (left.Payload == "first")
                {
                    entered.TrySetResult();
                    if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Selector was not released.");
                }
                return left.Key;
            }, right => right.Key);
        var output = RoutingTestSink.Link(node.Output);
        Task<bool>? third = null;
        try
        {
            (await node.Left.SendAsync(Message(new LeftMessage("key", "first")))).ShouldBeTrue();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            (await node.Left.SendAsync(Message(new LeftMessage("key", "second")))).ShouldBeTrue();
            third = node.Left.SendAsync(Message(new LeftMessage("key", "third")));
            third.IsCompleted.ShouldBeFalse();
        }
        finally { release.Set(); }

        (await third!.WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeTrue();
        node.Complete();
        var results = await RoutingTestSink.DrainUntilCompletedAsync(output);

        results.Select(message => message.Value.ShouldNotBeNull()
            .Deserialize<FlowJoinTimedOutOutcome<LeftMessage, RightMessage>>().ShouldNotBeNull()
            .Timeout.Left!.Payload).ShouldBe(["first", "second", "third"]);
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Join_fault_waits_for_active_selector_before_disposal_completes()
    {
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var node = new JoinNode<LeftMessage, RightMessage>(new JoinRoutingOptions(), left =>
        {
            entered.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Selector was not released.");
            return left.Key;
        }, right => right.Key);
        Task? disposal = null;
        var failure = new InvalidOperationException("Stop active join.");
        try
        {
            (await node.Left.SendAsync(Message(new LeftMessage("key", "left")))).ShouldBeTrue();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            node.Fault(failure);
            disposal = node.DisposeAsync().AsTask();
            node.Completion.IsCompleted.ShouldBeFalse();
            disposal.IsCompleted.ShouldBeFalse();
        }
        finally
        {
            release.Set();
            await (disposal ?? node.DisposeAsync().AsTask()).WaitAsync(TimeSpan.FromSeconds(5));
        }

        var observed = await Should.ThrowAsync<InvalidOperationException>(async () => await node.Completion);
        observed.ShouldBeSameAs(failure);
    }

    private static FlowMessage Message<T>(T value) => FlowMessage.Create(FlowValue.From(value));

    private static FlowJoinResult<LeftMessage, RightMessage> Matched(
        FlowMessage message)
        => message.Value.ShouldNotBeNull().Deserialize<FlowJoinMatchedOutcome<LeftMessage, RightMessage>>().ShouldNotBeNull().Match;

    private static JoinNode<LeftMessage, RightMessage> CreateNode(
        Func<JoinRoutingOptions, JoinRoutingOptions>? configure = null,
        TimeProvider? clock = null)
    {
        var options = configure?.Invoke(new JoinRoutingOptions()) ?? new JoinRoutingOptions();
        return new JoinNode<LeftMessage, RightMessage>(
            options,
            left => left.Key,
            right => right.Key,
            clock: clock);
    }
}
