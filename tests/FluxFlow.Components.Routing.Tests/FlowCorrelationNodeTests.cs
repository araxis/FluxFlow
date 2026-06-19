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

// Correlation is a single-stream node: it pairs request/response messages by a key
// extracted from each payload, with the side derived from each payload too. Every test
// news the node directly with key/side selectors — no engine.
public sealed class FlowCorrelationNodeTests
{
    private sealed record CorrelationMessage(string Key, string Side, string Payload);

    [Fact]
    public async Task Correlation_MatchesRequestAndResponseByKey_CarryingRequestCorrelation()
    {
        await using var node = CreateNode();
        var matched = RoutingTestSink.Link(node.Matched);
        node.Timeouts.LinkTo(DataflowBlock.NullTarget<FlowMessage<FlowCorrelationTimeout<CorrelationMessage>>>());

        var request = FlowMessage.Create(new CorrelationMessage("A-100", "request", "start"));
        await node.Input.SendAsync(request);
        await node.Input.SendAsync(FlowMessage.Create(new CorrelationMessage("A-100", "response", "done")));
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var match = (await RoutingTestSink.DrainUntilCompletedAsync(matched)).ShouldHaveSingleItem();
        match.Payload.Key.ShouldBe("A-100");
        match.Payload.Request.Payload.ShouldBe("start");
        match.Payload.Response.Payload.ShouldBe("done");
        match.Payload.Elapsed.ShouldBeGreaterThanOrEqualTo(TimeSpan.Zero);
        match.CorrelationId.ShouldBe(request.CorrelationId);
    }

    [Fact]
    public async Task Correlation_MatchesOutOfOrderResponseAndRequest()
    {
        await using var node = CreateNode();
        var matched = RoutingTestSink.Link(node.Matched);
        node.Timeouts.LinkTo(DataflowBlock.NullTarget<FlowMessage<FlowCorrelationTimeout<CorrelationMessage>>>());

        await node.Input.SendAsync(FlowMessage.Create(new CorrelationMessage("A-100", "response", "done")));
        await node.Input.SendAsync(FlowMessage.Create(new CorrelationMessage("A-100", "request", "start")));
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var match = (await RoutingTestSink.DrainUntilCompletedAsync(matched)).ShouldHaveSingleItem();
        match.Payload.Request.Payload.ShouldBe("start");
        match.Payload.Response.Payload.ShouldBe("done");
    }

    [Fact]
    public async Task Correlation_EmitsTimeoutsForUnmatchedInputsOnCompletion()
    {
        await using var node = CreateNode(o => o with { TimeoutMilliseconds = 10 });
        node.Matched.LinkTo(DataflowBlock.NullTarget<FlowMessage<FlowCorrelationMatch<CorrelationMessage>>>());
        var timeouts = RoutingTestSink.Link(node.Timeouts);

        var request = FlowMessage.Create(new CorrelationMessage("A-100", "request", "start"));
        await node.Input.SendAsync(request);
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var timeout = (await RoutingTestSink.DrainUntilCompletedAsync(timeouts)).ShouldHaveSingleItem();
        timeout.Payload.Key.ShouldBe("A-100");
        timeout.Payload.Side.ShouldBe("request");
        timeout.Payload.Value.Payload.ShouldBe("start");
        timeout.Payload.Timeout.ShouldBe(TimeSpan.FromMilliseconds(10));
        timeout.CorrelationId.ShouldBe(request.CorrelationId);
    }

    [Fact]
    public async Task Correlation_ExpiresPendingInputsBeforeProcessingNextInput()
    {
        // ManualTimeProvider's timer never fires, so the first input can only expire via the
        // EmitExpired at the start of processing the second input -- exactly what this verifies.
        var startedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var clock = new ManualTimeProvider(startedAt);
        var firstEvaluated = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var node = new FlowCorrelationNode<CorrelationMessage>(
            new CorrelationRoutingOptions { TimeoutMilliseconds = 25 },
            input =>
            {
                if (input.Payload == "start")
                {
                    firstEvaluated.TrySetResult(null);
                }

                return input.Key;
            },
            input => input.Side,
            clock: clock);
        node.Matched.LinkTo(DataflowBlock.NullTarget<FlowMessage<FlowCorrelationMatch<CorrelationMessage>>>());
        var timeouts = RoutingTestSink.Link(node.Timeouts);

        await node.Input.SendAsync(FlowMessage.Create(new CorrelationMessage("A-100", "request", "start")));
        await firstEvaluated.Task.WaitAsync(TimeSpan.FromSeconds(30));
        clock.SetUtcNow(startedAt.AddMilliseconds(100));
        await node.Input.SendAsync(FlowMessage.Create(new CorrelationMessage("A-100", "response", "done")));
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var emitted = await RoutingTestSink.DrainUntilCompletedAsync(timeouts);
        emitted.Count.ShouldBe(2);
        emitted[0].Payload.Side.ShouldBe("request");
        emitted[1].Payload.Side.ShouldBe("response");
    }

    [Fact]
    public async Task Correlation_DuplicateSideWarnsAndKeepsOriginalDeadline()
    {
        var startedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var clock = new ManualTimeProvider(startedAt);
        var firstEvaluated = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var duplicateEvaluated = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var node = new FlowCorrelationNode<CorrelationMessage>(
            new CorrelationRoutingOptions { TimeoutMilliseconds = 100 },
            input =>
            {
                if (input.Payload == "first")
                {
                    firstEvaluated.TrySetResult(null);
                }

                if (input.Payload == "second")
                {
                    duplicateEvaluated.TrySetResult(null);
                }

                return input.Key;
            },
            input => input.Side,
            clock: clock);
        node.Matched.LinkTo(DataflowBlock.NullTarget<FlowMessage<FlowCorrelationMatch<CorrelationMessage>>>());
        var errors = RoutingTestSink.Link(node.Errors);
        var events = RoutingTestSink.Link(node.Events);
        var timeouts = RoutingTestSink.Link(node.Timeouts);

        await node.Input.SendAsync(FlowMessage.Create(new CorrelationMessage("A-100", "request", "first")));
        await firstEvaluated.Task.WaitAsync(TimeSpan.FromSeconds(30));
        clock.SetUtcNow(startedAt.AddMilliseconds(50));
        await node.Input.SendAsync(FlowMessage.Create(new CorrelationMessage("A-100", "request", "second")));
        await duplicateEvaluated.Task.WaitAsync(TimeSpan.FromSeconds(30));
        clock.SetUtcNow(startedAt.AddMilliseconds(120));
        await node.Input.SendAsync(FlowMessage.Create(new CorrelationMessage("B-200", "request", "other")));

        var timeout = await timeouts.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        (await RoutingTestSink.DrainUntilCompletedAsync(errors)).ShouldBeEmpty();
        var duplicate = (await RoutingTestSink.DrainUntilCompletedAsync(events))
            .First(e => e.Name == RoutingDiagnosticNames.CorrelationDuplicateSide);
        duplicate.Level.ShouldBe(FlowEventLevel.Warning);
        duplicate.Attributes["key"].ShouldBe("A-100");
        duplicate.Attributes["side"].ShouldBe("request");
        timeout.Payload.Key.ShouldBe("A-100");
        timeout.Payload.Side.ShouldBe("request");
        timeout.Payload.Value.Payload.ShouldBe("second");
        timeout.Payload.ReceivedAt.ShouldBe(startedAt);
        timeout.Payload.TimedOutAt.ShouldBe(startedAt.AddMilliseconds(120));
    }

    [Fact]
    public async Task Correlation_ReportsKeyFailureAndContinues()
    {
        await using var node = new FlowCorrelationNode<CorrelationMessage>(
            new CorrelationRoutingOptions { ExpressionName = "pairing" },
            input => input.Payload == "throw"
                ? throw new InvalidOperationException("key failed")
                : input.Key,
            input => input.Side);
        var errors = RoutingTestSink.Link(node.Errors);
        var matched = RoutingTestSink.Link(node.Matched);
        node.Timeouts.LinkTo(DataflowBlock.NullTarget<FlowMessage<FlowCorrelationTimeout<CorrelationMessage>>>());

        var throwing = FlowMessage.Create(new CorrelationMessage("A-100", "request", "throw"));
        await node.Input.SendAsync(throwing);
        await node.Input.SendAsync(FlowMessage.Create(new CorrelationMessage("A-101", "request", "start")));
        await node.Input.SendAsync(FlowMessage.Create(new CorrelationMessage("A-101", "response", "done")));
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var error = (await RoutingTestSink.DrainUntilCompletedAsync(errors)).First();
        error.Code.ShouldBe(RoutingErrorCodes.CorrelationKeyFailed);
        error.Context!.ShouldContain("expressionName=pairing");
        (await RoutingTestSink.DrainUntilCompletedAsync(matched)).ShouldHaveSingleItem()
            .Payload.Key.ShouldBe("A-101");
    }

    [Fact]
    public async Task Correlation_RejectsInvalidSideAndContinues()
    {
        await using var node = CreateNode();
        var errors = RoutingTestSink.Link(node.Errors);
        var matched = RoutingTestSink.Link(node.Matched);
        node.Timeouts.LinkTo(DataflowBlock.NullTarget<FlowMessage<FlowCorrelationTimeout<CorrelationMessage>>>());

        await node.Input.SendAsync(FlowMessage.Create(new CorrelationMessage("A-100", "other", "bad")));
        await node.Input.SendAsync(FlowMessage.Create(new CorrelationMessage("A-100", "request", "start")));
        await node.Input.SendAsync(FlowMessage.Create(new CorrelationMessage("A-100", "response", "done")));
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        (await RoutingTestSink.DrainUntilCompletedAsync(errors)).First()
            .Code.ShouldBe(RoutingErrorCodes.CorrelationInvalidSide);
        (await RoutingTestSink.DrainUntilCompletedAsync(matched)).ShouldHaveSingleItem()
            .Payload.Key.ShouldBe("A-100");
    }

    [Fact]
    public async Task Correlation_ReportsCapacityLimitAndContinues()
    {
        await using var node = CreateNode(o => o with { MaxPending = 1 });
        var errors = RoutingTestSink.Link(node.Errors);
        node.Matched.LinkTo(DataflowBlock.NullTarget<FlowMessage<FlowCorrelationMatch<CorrelationMessage>>>());
        node.Timeouts.LinkTo(DataflowBlock.NullTarget<FlowMessage<FlowCorrelationTimeout<CorrelationMessage>>>());

        await node.Input.SendAsync(FlowMessage.Create(new CorrelationMessage("A-100", "request", "start")));
        await node.Input.SendAsync(FlowMessage.Create(new CorrelationMessage("A-101", "request", "next")));
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var error = (await RoutingTestSink.DrainUntilCompletedAsync(errors)).First();
        error.Code.ShouldBe(RoutingErrorCodes.CorrelationCapacityExceeded);
        error.Context!.ShouldContain("key=A-101");
    }

    [Fact]
    public async Task Correlation_EmitsMatchedEvent()
    {
        await using var node = CreateNode(o => o with { ExpressionId = "corr-v1" });
        var events = RoutingTestSink.Link(node.Events);
        node.Matched.LinkTo(DataflowBlock.NullTarget<FlowMessage<FlowCorrelationMatch<CorrelationMessage>>>());
        node.Timeouts.LinkTo(DataflowBlock.NullTarget<FlowMessage<FlowCorrelationTimeout<CorrelationMessage>>>());

        await node.Input.SendAsync(FlowMessage.Create(new CorrelationMessage("A-100", "request", "start")));
        await node.Input.SendAsync(FlowMessage.Create(new CorrelationMessage("A-100", "response", "done")));
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var matchedEvent = (await RoutingTestSink.DrainUntilCompletedAsync(events))
            .First(e => e.Name == RoutingDiagnosticNames.CorrelationMatched);
        matchedEvent.Attributes["key"].ShouldBe("A-100");
        matchedEvent.Attributes["expressionId"].ShouldBe("corr-v1");
    }

    [Fact]
    public async Task Correlation_UsesConfiguredClockForTimeoutDelay()
    {
        var startedAt = DateTimeOffset.Parse("2026-01-01T00:00:05Z");
        var clock = new TrackingFakeTimeProvider(startedAt);
        await using var node = CreateNode(o => o with { TimeoutMilliseconds = 25 }, clock);
        node.Matched.LinkTo(DataflowBlock.NullTarget<FlowMessage<FlowCorrelationMatch<CorrelationMessage>>>());
        var timeouts = RoutingTestSink.Link(node.Timeouts);

        var timerScheduled = clock.NextTimerScheduled;
        await node.Input.SendAsync(FlowMessage.Create(new CorrelationMessage("A-100", "request", "start")));
        await timerScheduled.WaitAsync(TimeSpan.FromSeconds(30));
        timeouts.TryReceive(out _).ShouldBeFalse();

        clock.Advance(TimeSpan.FromMilliseconds(25));
        var timeout = await timeouts.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        timeout.Payload.Key.ShouldBe("A-100");
        timeout.Payload.Side.ShouldBe("request");
        timeout.Payload.ReceivedAt.ShouldBe(startedAt);
        timeout.Payload.TimedOutAt.ShouldBe(startedAt.AddMilliseconds(25));
    }

    [Fact]
    public async Task Correlation_UsesConfiguredClockForMatchTimestamps()
    {
        var timestamp = DateTimeOffset.Parse("2026-01-01T00:00:04Z");
        // Never advanced: the match is driven purely by the two inputs.
        var clock = new FakeTimeProvider(timestamp);
        await using var node = CreateNode(clock: clock);
        var matched = RoutingTestSink.Link(node.Matched);
        node.Timeouts.LinkTo(DataflowBlock.NullTarget<FlowMessage<FlowCorrelationTimeout<CorrelationMessage>>>());

        await node.Input.SendAsync(FlowMessage.Create(new CorrelationMessage("A-100", "request", "start")));
        await node.Input.SendAsync(FlowMessage.Create(new CorrelationMessage("A-100", "response", "done")));
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var match = (await RoutingTestSink.DrainUntilCompletedAsync(matched)).ShouldHaveSingleItem();
        match.Payload.RequestReceivedAt.ShouldBe(timestamp);
        match.Payload.ResponseReceivedAt.ShouldBe(timestamp);
        match.Payload.MatchedAt.ShouldBe(timestamp);
    }

    [Fact]
    public async Task Correlation_DisposeAfterFaultDoesNotThrow()
    {
        var node = CreateNode();

        node.Fault(new InvalidOperationException("boom"));
        await node.DisposeAsync();

        node.Completion.IsFaulted.ShouldBeTrue();
    }

    [Fact]
    public void Correlation_RejectsEqualSides()
        => Should.Throw<ArgumentException>(
            () => CreateNode(o => o with { RequestSide = "message", ResponseSide = "message" }))
            .Message.ShouldContain("different");

    [Fact]
    public void Correlation_RejectsNullOptions()
        => Should.Throw<ArgumentNullException>(
            () => new FlowCorrelationNode<CorrelationMessage>(null!, input => input.Key, input => input.Side));

    [Fact]
    public void Correlation_RejectsNullKeySelector()
        => Should.Throw<ArgumentNullException>(
            () => new FlowCorrelationNode<CorrelationMessage>(
                new CorrelationRoutingOptions(), null!, input => input.Side));

    private static FlowCorrelationNode<CorrelationMessage> CreateNode(
        Func<CorrelationRoutingOptions, CorrelationRoutingOptions>? configure = null,
        TimeProvider? clock = null)
    {
        var options = configure?.Invoke(new CorrelationRoutingOptions()) ?? new CorrelationRoutingOptions();
        return new FlowCorrelationNode<CorrelationMessage>(
            options,
            input => input.Key,
            input => input.Side,
            clock: clock);
    }

    // Wall clock moved explicitly via SetUtcNow while the scheduled timer never fires —
    // expiry is driven through the input-time path.
    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private readonly object _gate = new();
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow()
        {
            lock (_gate)
            {
                return _utcNow;
            }
        }

        public void SetUtcNow(DateTimeOffset utcNow)
        {
            lock (_gate)
            {
                _utcNow = utcNow;
            }
        }

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
            => new NeverFiringTimer();

        private sealed class NeverFiringTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;

            public void Dispose()
            {
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
