using FluxFlow.Components.Resilience.Contracts;
using FluxFlow.Components.Resilience.Diagnostics;
using FluxFlow.Components.Resilience.Nodes;
using FluxFlow.Components.Resilience.Options;
using FluxFlow.Data;
using FluxFlow.Nodes;
using FluxFlow.Resilience;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Components.Resilience.Tests;

public sealed class FlowRetryNodeTests
{
    [Fact]
    public async Task Ack_completes_matching_attempt_with_stable_trace()
    {
        var clock = CreateClock();
        await using var node = CreateNode(clock);
        var output = LinkOutput(node);
        var message = FlowMessage.Create("payload");

        (await node.Input.SendAsync(message)).ShouldBeTrue();
        var attempt = await ReceiveAsync(output);
        attempt.Value.Status.ShouldBe(RetrySignalStatus.Attempt);
        attempt.TraceId.ShouldBe(message.TraceId);
        attempt.Value.Attempt.ShouldBe(1);
        attempt.CausationId.ShouldBe(message.MessageId);
        attempt.Headers.Single(header =>
            header.Key.StartsWith("flow.retry.attempt.", StringComparison.Ordinal))
            .Value.ShouldBe("1");

        var acknowledgement = attempt.With("ack");
        (await node.Ack.SendAsync(acknowledgement)).ShouldBeTrue();
        var completed = await ReceiveAsync(output);
        completed.Value.Status.ShouldBe(RetrySignalStatus.Completed);
        completed.IsError.ShouldBeFalse();
        completed.TraceId.ShouldBe(message.TraceId);
        completed.CausationId.ShouldBe(acknowledgement.MessageId);
        completed.Value.Value.ShouldBe(message.Value);

        node.Complete();
        await node.Completion;
    }

    [Fact]
    public async Task Nak_schedules_next_attempt_and_late_feedback_is_rejected()
    {
        var clock = CreateClock();
        await using var node = CreateNode(clock);
        var output = LinkOutput(node);
        var events = LinkEvents(node);
        var message = FlowMessage.Create("payload");

        await node.Input.SendAsync(message);
        var attemptOne = await ReceiveAsync(output);
        var nak = attemptOne.With("nak");
        (await node.Nak.SendAsync(nak)).ShouldBeTrue();

        var scheduled = await ReceiveAsync(output);
        scheduled.IsError.ShouldBeTrue();
        scheduled.Error!.Code.ShouldBe(RetryErrorCodeNames.Nak);
        scheduled.Error.Details.ShouldNotBeNull()
            .GetProperty("nextDelayMilliseconds").GetDouble().ShouldBe(1_000d);
        scheduled.CausationId.ShouldBe(nak.MessageId);

        clock.Advance(TimeSpan.FromSeconds(1));
        var attemptTwo = await ReceiveAsync(output);
        attemptTwo.Value.Status.ShouldBe(RetrySignalStatus.Attempt);
        attemptTwo.Value.Attempt.ShouldBe(2);
        attemptTwo.CausationId.ShouldBe(scheduled.MessageId);

        (await node.Ack.SendAsync(attemptOne.With("late"))).ShouldBeFalse();
        (await node.Ack.SendAsync(attemptTwo.With("ack"))).ShouldBeTrue();
        (await ReceiveAsync(output)).Value.Status.ShouldBe(RetrySignalStatus.Completed);
        (await ReceiveEventAsync(events, RetryDiagnosticNames.FeedbackIgnored)).Name
            .ShouldBe(RetryDiagnosticNames.FeedbackIgnored);

        node.Complete();
        await node.Completion;
    }

    [Fact]
    public async Task Attempt_timeouts_retry_then_exhaust_exactly_once()
    {
        var clock = CreateClock();
        await using var node = CreateNode(clock, maximumAttempts: 2, attemptTimeoutMilliseconds: 100);
        var output = LinkOutput(node);

        await node.Input.SendAsync(FlowMessage.Create("payload"));
        (await ReceiveAsync(output)).Value.Status.ShouldBe(RetrySignalStatus.Attempt);

        clock.Advance(TimeSpan.FromMilliseconds(100));
        var scheduled = await ReceiveAsync(output);
        scheduled.Error!.Code.ShouldBe(RetryErrorCodeNames.Timeout);

        clock.Advance(TimeSpan.FromSeconds(1));
        (await ReceiveAsync(output)).Value.Attempt.ShouldBe(2);
        clock.Advance(TimeSpan.FromMilliseconds(100));
        var exhausted = await ReceiveAsync(output);
        exhausted.Error!.Code.ShouldBe(RetryErrorCodeNames.Exhausted);

        output.TryReceive(out _).ShouldBeFalse();
        node.Complete();
        await node.Completion;
    }

    [Fact]
    public async Task Capacity_rejection_is_normal_output_data()
    {
        var clock = CreateClock();
        await using var node = CreateNode(clock, capacity: 1);
        var output = LinkOutput(node);
        var first = FlowMessage.Create("first");
        var second = FlowMessage.Create("second");

        await node.Input.SendAsync(first);
        var firstAttempt = await ReceiveAsync(output);
        await node.Input.SendAsync(second);
        var rejected = await ReceiveAsync(output);

        rejected.IsError.ShouldBeTrue();
        rejected.Error!.Code.ShouldBe(RetryErrorCodeNames.CapacityReached);
        rejected.TraceId.ShouldBe(second.TraceId);

        await node.Ack.SendAsync(firstAttempt.With("ack"));
        (await ReceiveAsync(output)).Value.Status.ShouldBe(RetrySignalStatus.Completed);
        node.Complete();
        await node.Completion;
    }

    [Fact]
    public async Task Duplicate_trace_is_rejected_without_replacing_pending_operation()
    {
        var clock = CreateClock();
        await using var node = CreateNode(clock);
        var output = LinkOutput(node);
        var traceId = TraceId.New();
        var first = FlowMessage.Create("first", traceId: traceId);
        var duplicate = FlowMessage.Create("duplicate", traceId: traceId);

        await node.Input.SendAsync(first);
        var attempt = await ReceiveAsync(output);
        await node.Input.SendAsync(duplicate);
        var rejected = await ReceiveAsync(output);
        rejected.Error!.Code.ShouldBe(RetryErrorCodeNames.Duplicate);

        await node.Ack.SendAsync(attempt.With("ack"));
        (await ReceiveAsync(output)).Value.Status.ShouldBe(RetrySignalStatus.Completed);
        node.Complete();
        await node.Completion;
    }

    [Fact]
    public async Task Cancel_during_wait_emits_one_cancelled_result()
    {
        var clock = CreateClock();
        await using var node = CreateNode(clock);
        var output = LinkOutput(node);

        await node.Input.SendAsync(FlowMessage.Create("payload"));
        var attempt = await ReceiveAsync(output);
        await node.Nak.SendAsync(attempt.With("nak"));
        (await ReceiveAsync(output)).Error!.Code.ShouldBe(RetryErrorCodeNames.Nak);

        (await node.Cancel.SendAsync(attempt.With("cancel"))).ShouldBeTrue();
        var cancelled = await ReceiveAsync(output);
        cancelled.Error!.Code.ShouldBe(RetryErrorCodeNames.Cancelled);

        clock.Advance(TimeSpan.FromMinutes(1));
        output.TryReceive(out _).ShouldBeFalse();
        node.Complete();
        await node.Completion;
    }

    [Fact]
    public async Task Competing_feedback_settles_the_attempt_exactly_once()
    {
        var clock = CreateClock();
        await using var node = CreateNode(clock, maximumAttempts: 1);
        var output = LinkOutput(node);

        await node.Input.SendAsync(FlowMessage.Create("payload"));
        var attempt = await ReceiveAsync(output);

        var feedback = await Task.WhenAll(
            node.Ack.SendAsync(attempt.With("ack")).AsTask(),
            node.Nak.SendAsync(attempt.With("nak")).AsTask(),
            node.Cancel.SendAsync(attempt.With("cancel")).AsTask());

        feedback.Count(static accepted => accepted).ShouldBe(1);
        var terminal = await ReceiveAsync(output);
        if (terminal.IsError)
        {
            terminal.Error!.Code.ShouldBeOneOf(
                RetryErrorCodeNames.Exhausted,
                RetryErrorCodeNames.Cancelled);
        }
        else
        {
            terminal.Value.Status.ShouldBe(RetrySignalStatus.Completed);
        }
        output.TryReceive(out _).ShouldBeFalse();

        node.Complete();
        await node.Completion;
    }

    [Fact]
    public async Task Completion_settles_pending_operation_before_output_completes()
    {
        var clock = CreateClock();
        await using var node = CreateNode(clock);
        var output = LinkOutput(node);

        await node.Input.SendAsync(FlowMessage.Create("payload"));
        (await ReceiveAsync(output)).Value.Status.ShouldBe(RetrySignalStatus.Attempt);
        node.Complete();

        var cancelled = await ReceiveAsync(output);
        cancelled.Error!.Code.ShouldBe(RetryErrorCodeNames.Stopped);
        await node.Completion;
        await output.Completion;
    }

    [Fact]
    public async Task Feedback_without_attempt_header_is_rejected()
    {
        var clock = CreateClock();
        await using var node = CreateNode(clock);
        var events = LinkEvents(node);

        (await node.Ack.SendAsync(FlowMessage.Create("ack"))).ShouldBeFalse();
        (await ReceiveEventAsync(events, RetryDiagnosticNames.FeedbackIgnored)).Name
            .ShouldBe(RetryDiagnosticNames.FeedbackIgnored);

        node.Complete();
        await node.Completion;
    }

    [Fact]
    public void Invalid_options_are_rejected()
    {
        Should.Throw<ArgumentOutOfRangeException>(() =>
            new FlowRetryNode<string>(new FlowRetryOptions { Capacity = 0 }));
        Should.Throw<ArgumentOutOfRangeException>(() =>
            new FlowRetryNode<string>(new FlowRetryOptions { AttemptTimeoutMilliseconds = 0 }));
        Should.Throw<ArgumentOutOfRangeException>(() =>
            new FlowRetryNode<string>(new FlowRetryOptions { MaximumAttempts = 0 }));
    }

    private static FlowRetryNode<string> CreateNode(
        FakeTimeProvider clock,
        int maximumAttempts = 3,
        int attemptTimeoutMilliseconds = 10_000,
        int capacity = 8)
        => new(
            new FlowRetryOptions
            {
                Name = "test",
                Strategy = RetryBackoffStrategy.Fixed,
                InitialDelayMilliseconds = 1_000,
                MaximumDelayMilliseconds = 1_000,
                MaximumAttempts = maximumAttempts,
                AttemptTimeoutMilliseconds = attemptTimeoutMilliseconds,
                Capacity = capacity
            },
            clock,
            new ConstantJitterSource());

    private static FakeTimeProvider CreateClock()
        => new(new DateTimeOffset(2026, 7, 26, 10, 0, 0, TimeSpan.Zero));

    private static BufferBlock<FlowMessage<RetrySignal<string>>> LinkOutput(
        FlowRetryNode<string> node)
    {
        var target = new BufferBlock<FlowMessage<RetrySignal<string>>>();
        node.Output.LinkTo(target, new DataflowLinkOptions { PropagateCompletion = true });
        return target;
    }

    private static BufferBlock<FlowEvent> LinkEvents(FlowRetryNode<string> node)
    {
        var target = new BufferBlock<FlowEvent>();
        node.Events.LinkTo(target, new DataflowLinkOptions { PropagateCompletion = true });
        return target;
    }

    private static Task<FlowMessage<RetrySignal<string>>> ReceiveAsync(
        BufferBlock<FlowMessage<RetrySignal<string>>> output)
        => output.ReceiveAsync(TimeSpan.FromSeconds(5));

    private static async Task<FlowEvent> ReceiveEventAsync(BufferBlock<FlowEvent> events, string name)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (true)
        {
            var @event = await events.ReceiveAsync(timeout.Token);
            if (string.Equals(@event.Name, name, StringComparison.Ordinal))
                return @event;
        }
    }

    private sealed class ConstantJitterSource : IRetryJitterSource
    {
        public double NextSample() => 0.5;
    }
}
