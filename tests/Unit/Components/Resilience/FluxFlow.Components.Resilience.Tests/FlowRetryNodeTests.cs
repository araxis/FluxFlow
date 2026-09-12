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
        var message = FlowMessage.Create(FlowValue.From("payload"));

        (await node.Input.SendAsync(message)).ShouldBeTrue();
        var attempt = await ReceiveAsync(output);
        Read(attempt).Status.ShouldBe(RetrySignalStatus.Attempt);
        attempt.TraceId.ShouldBe(message.TraceId);
        Read(attempt).Attempt.ShouldBe(1);
        attempt.CausationId.ShouldBe(message.MessageId);
        attempt.Headers.Single(header =>
            header.Key.StartsWith("flow.retry.attempt.", StringComparison.Ordinal))
            .Value.ShouldBe("1");

        var acknowledgement = attempt.With(FlowValue.From("ack"));
        (await node.Ack.SendAsync(acknowledgement)).ShouldBeTrue();
        var completed = await ReceiveAsync(output);
        Read(completed).Status.ShouldBe(RetrySignalStatus.Completed);
        completed.IsError.ShouldBeFalse();
        completed.TraceId.ShouldBe(message.TraceId);
        completed.CausationId.ShouldBe(acknowledgement.MessageId);
        Read(completed).Value.ShouldBe(message.Value!.Deserialize<string>());

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
        var message = FlowMessage.Create(FlowValue.From("payload"));

        await node.Input.SendAsync(message);
        var attemptOne = await ReceiveAsync(output);
        var nak = attemptOne.With(FlowValue.From("nak"));
        (await node.Nak.SendAsync(nak)).ShouldBeTrue();

        var scheduled = await ReceiveAsync(output);
        scheduled.IsError.ShouldBeTrue();
        scheduled.Error!.Code.ShouldBe(RetryErrorCodeNames.Nak);
        scheduled.Error.Details.ShouldNotBeNull()
            .GetProperty("nextDelayMilliseconds").GetDouble().ShouldBe(1_000d);
        scheduled.CausationId.ShouldBe(nak.MessageId);

        clock.Advance(TimeSpan.FromSeconds(1));
        var attemptTwo = await ReceiveAsync(output);
        Read(attemptTwo).Status.ShouldBe(RetrySignalStatus.Attempt);
        Read(attemptTwo).Attempt.ShouldBe(2);
        attemptTwo.CausationId.ShouldBe(scheduled.MessageId);

        (await node.Ack.SendAsync(attemptOne.With(FlowValue.From("late")))).ShouldBeTrue();
        (await node.Ack.SendAsync(attemptTwo.With(FlowValue.From("ack")))).ShouldBeTrue();
        Read(await ReceiveAsync(output)).Status.ShouldBe(RetrySignalStatus.Completed);
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

        await node.Input.SendAsync(FlowMessage.Create(FlowValue.From("payload")));
        Read(await ReceiveAsync(output)).Status.ShouldBe(RetrySignalStatus.Attempt);

        clock.Advance(TimeSpan.FromMilliseconds(100));
        var scheduled = await ReceiveAsync(output);
        scheduled.Error!.Code.ShouldBe(RetryErrorCodeNames.Timeout);

        clock.Advance(TimeSpan.FromSeconds(1));
        Read(await ReceiveAsync(output)).Attempt.ShouldBe(2);
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
        var first = FlowMessage.Create(FlowValue.From("first"));
        var second = FlowMessage.Create(FlowValue.From("second"));

        await node.Input.SendAsync(first);
        var firstAttempt = await ReceiveAsync(output);
        await node.Input.SendAsync(second);
        var rejected = await ReceiveAsync(output);

        rejected.IsError.ShouldBeTrue();
        rejected.Error!.Code.ShouldBe(RetryErrorCodeNames.CapacityReached);
        rejected.TraceId.ShouldBe(second.TraceId);

        await node.Ack.SendAsync(firstAttempt.With(FlowValue.From("ack")));
        Read(await ReceiveAsync(output)).Status.ShouldBe(RetrySignalStatus.Completed);
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
        var first = FlowMessage.Create(FlowValue.From("first"), traceId: traceId);
        var duplicate = FlowMessage.Create(FlowValue.From("duplicate"), traceId: traceId);

        await node.Input.SendAsync(first);
        var attempt = await ReceiveAsync(output);
        await node.Input.SendAsync(duplicate);
        var rejected = await ReceiveAsync(output);
        rejected.Error!.Code.ShouldBe(RetryErrorCodeNames.Duplicate);

        await node.Ack.SendAsync(attempt.With(FlowValue.From("ack")));
        Read(await ReceiveAsync(output)).Status.ShouldBe(RetrySignalStatus.Completed);
        node.Complete();
        await node.Completion;
    }

    [Fact]
    public async Task Cancel_during_wait_emits_one_cancelled_result()
    {
        var clock = CreateClock();
        await using var node = CreateNode(clock);
        var output = LinkOutput(node);

        await node.Input.SendAsync(FlowMessage.Create(FlowValue.From("payload")));
        var attempt = await ReceiveAsync(output);
        await node.Nak.SendAsync(attempt.With(FlowValue.From("nak")));
        (await ReceiveAsync(output)).Error!.Code.ShouldBe(RetryErrorCodeNames.Nak);

        (await node.Cancel.SendAsync(attempt.With(FlowValue.From("cancel")))).ShouldBeTrue();
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

        await node.Input.SendAsync(FlowMessage.Create(FlowValue.From("payload")));
        var attempt = await ReceiveAsync(output);

        var feedback = await Task.WhenAll(
            node.Ack.SendAsync(attempt.With(FlowValue.From("ack"))),
            node.Nak.SendAsync(attempt.With(FlowValue.From("nak"))),
            node.Cancel.SendAsync(attempt.With(FlowValue.From("cancel"))));

        feedback.ShouldAllBe(static admitted => admitted);
        var terminal = await ReceiveAsync(output);
        if (terminal.IsError)
        {
            terminal.Error!.Code.ShouldBeOneOf(
                RetryErrorCodeNames.Exhausted,
                RetryErrorCodeNames.Cancelled);
        }
        else
        {
            Read(terminal).Status.ShouldBe(RetrySignalStatus.Completed);
        }
        node.Complete();
        await node.Completion;
        await output.Completion;
        output.TryReceive(out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Completion_settles_pending_operation_before_output_completes()
    {
        var clock = CreateClock();
        await using var node = CreateNode(clock);
        var output = LinkOutput(node);

        await node.Input.SendAsync(FlowMessage.Create(FlowValue.From("payload")));
        Read(await ReceiveAsync(output)).Status.ShouldBe(RetrySignalStatus.Attempt);
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

        (await node.Ack.SendAsync(FlowMessage.Create(FlowValue.From("ack")))).ShouldBeTrue();
        (await ReceiveEventAsync(events, RetryDiagnosticNames.FeedbackIgnored)).Name
            .ShouldBe(RetryDiagnosticNames.FeedbackIgnored);

        node.Complete();
        await node.Completion;
    }

    [Fact]
    public void Invalid_options_are_rejected()
    {
        Should.Throw<ArgumentOutOfRangeException>(() =>
            new FlowRetryNode(new FlowRetryOptions { Capacity = 0 }));
        Should.Throw<ArgumentOutOfRangeException>(() =>
            new FlowRetryNode(new FlowRetryOptions { AttemptTimeoutMilliseconds = 0 }));
        Should.Throw<ArgumentOutOfRangeException>(() =>
            new FlowRetryNode(new FlowRetryOptions { MaximumAttempts = 0 }));
    }

    private static RetrySignal<string> Read(FlowMessage message)
        => message.Value.ShouldNotBeNull().Deserialize<RetrySignal<string>>().ShouldNotBeNull();

    private static FlowRetryNode CreateNode(
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

    private static BufferBlock<FlowMessage> LinkOutput(
        FlowRetryNode node)
    {
        var target = new BufferBlock<FlowMessage>();
        node.Output.LinkTo(target, new DataflowLinkOptions { PropagateCompletion = true });
        return target;
    }

    private static BufferBlock<FlowMessage> LinkEvents(FlowRetryNode node)
    {
        var target = new BufferBlock<FlowMessage>();
        node.Events.LinkTo(target, new DataflowLinkOptions { PropagateCompletion = true });
        return target;
    }

    private static Task<FlowMessage> ReceiveAsync(
        BufferBlock<FlowMessage> output)
        => output.ReceiveAsync(TimeSpan.FromSeconds(5));

    private static async Task<FlowEvent> ReceiveEventAsync(BufferBlock<FlowMessage> events, string name)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (true)
        {
            var @event = await events.ReceiveAsync(timeout.Token);
            if (string.Equals(@event.ToFlowEvent().Name, name, StringComparison.Ordinal))
                return @event.ToFlowEvent();
        }
    }

    private sealed class ConstantJitterSource : IRetryJitterSource
    {
        public double NextSample() => 0.5;
    }
}
