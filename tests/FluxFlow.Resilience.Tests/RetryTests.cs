using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;

namespace FluxFlow.Resilience.Tests;

public sealed class RetryTests
{
    [Theory]
    [InlineData(RetryBackoffStrategy.Fixed, 4, 2)]
    [InlineData(RetryBackoffStrategy.Linear, 4, 8)]
    [InlineData(RetryBackoffStrategy.Exponential, 4, 16)]
    public void Schedule_calculates_supported_backoff(
        RetryBackoffStrategy strategy,
        int retryNumber,
        int expectedSeconds)
    {
        var policy = new RetryPolicy
        {
            Strategy = strategy,
            InitialDelay = TimeSpan.FromSeconds(2),
            Increment = TimeSpan.FromSeconds(2),
            MaximumDelay = TimeSpan.FromMinutes(1)
        };

        RetrySchedule.GetDelay(policy, retryNumber).ShouldBe(TimeSpan.FromSeconds(expectedSeconds));
    }

    [Fact]
    public void Schedule_caps_overflow_and_final_jitter()
    {
        var policy = new RetryPolicy
        {
            Strategy = RetryBackoffStrategy.Exponential,
            InitialDelay = TimeSpan.MaxValue,
            MaximumDelay = TimeSpan.FromSeconds(10),
            JitterFactor = 1
        };

        RetrySchedule.GetDelay(policy, int.MaxValue, jitterSample: 1)
            .ShouldBe(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void Jitter_samples_are_deterministic_and_bounded()
    {
        var policy = new RetryPolicy
        {
            Strategy = RetryBackoffStrategy.Fixed,
            InitialDelay = TimeSpan.FromSeconds(10),
            MaximumDelay = TimeSpan.FromSeconds(20),
            JitterFactor = 0.2
        };

        RetrySchedule.GetDelay(policy, 1, 0).ShouldBe(TimeSpan.FromSeconds(8));
        RetrySchedule.GetDelay(policy, 1, 0.5).ShouldBe(TimeSpan.FromSeconds(10));
        RetrySchedule.GetDelay(policy, 1, 1).ShouldBe(TimeSpan.FromSeconds(12));
    }

    [Fact]
    public void Planner_enforces_attempt_and_duration_budgets()
    {
        var started = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var policy = new RetryPolicy
        {
            InitialDelay = TimeSpan.FromSeconds(2),
            MaximumDelay = TimeSpan.FromSeconds(2),
            MaximumAttempts = 3,
            MaximumDuration = TimeSpan.FromSeconds(5)
        };

        RetryPlanner.PlanAttempt(policy, 3, started, started).Kind.ShouldBe(RetryDirectiveKind.Wait);
        RetryPlanner.PlanAttempt(policy, 4, started, started).Kind.ShouldBe(RetryDirectiveKind.Exhausted);
        RetryPlanner.PlanAttempt(policy, 2, started, started.AddSeconds(4)).Kind
            .ShouldBe(RetryDirectiveKind.Exhausted);
    }

    [Fact]
    public void State_machine_transitions_attempt_wait_attempt_complete()
    {
        var started = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var machine = new RetryStateMachine(new RetryPolicy
        {
            InitialDelay = TimeSpan.FromSeconds(1),
            MaximumDelay = TimeSpan.FromSeconds(1)
        });

        var first = machine.Begin(started);
        var waiting = machine.AfterFailure(first.State, started);
        var second = machine.AfterDelay(waiting.State, started.AddSeconds(1));
        var completed = machine.Complete(second.State);

        first.Kind.ShouldBe(RetryDirectiveKind.Attempt);
        waiting.Kind.ShouldBe(RetryDirectiveKind.Wait);
        waiting.Attempt.ShouldBe(2);
        second.Kind.ShouldBe(RetryDirectiveKind.Attempt);
        completed.Kind.ShouldBe(RetryDirectiveKind.Complete);
        machine.AfterFailure(completed.State, started.AddSeconds(1)).Kind
            .ShouldBe(RetryDirectiveKind.Complete);
    }

    [Fact]
    public async Task Executor_retries_results_with_deterministic_time()
    {
        var time = new FakeTimeProvider();
        var executor = new RetryExecutor(
            new RetryPolicy
            {
                InitialDelay = TimeSpan.FromSeconds(1),
                MaximumDelay = TimeSpan.FromSeconds(1),
                MaximumAttempts = 3
            },
            time,
            new FixedJitterSource(0.5));
        var attempts = 0;

        var execution = executor.ExecuteAsync(
            (attempt, _) => ValueTask.FromResult(++attempts < 3 ? "retry" : $"success-{attempt}"),
            static result => result == "retry").AsTask();
        time.Advance(TimeSpan.FromSeconds(1));
        await Task.Yield();
        time.Advance(TimeSpan.FromSeconds(1));

        (await execution).ShouldBe("success-3");
        attempts.ShouldBe(3);
    }

    [Fact]
    public async Task Executor_honors_cancellation_while_waiting()
    {
        var time = new FakeTimeProvider();
        var executor = new RetryExecutor(
            new RetryPolicy
            {
                InitialDelay = TimeSpan.FromMinutes(1),
                MaximumDelay = TimeSpan.FromMinutes(1)
            },
            time,
            new FixedJitterSource(0.5));
        using var cancellation = new CancellationTokenSource();

        var execution = executor.ExecuteAsync(
            static (_, _) => ValueTask.FromResult(false),
            static result => !result,
            cancellationToken: cancellation.Token).AsTask();
        cancellation.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(execution);
    }

    [Fact]
    public async Task Executor_rethrows_last_retryable_exception_when_exhausted()
    {
        var executor = new RetryExecutor(
            new RetryPolicy
            {
                InitialDelay = TimeSpan.Zero,
                MaximumDelay = TimeSpan.Zero,
                MaximumAttempts = 2
            },
            jitterSource: new FixedJitterSource(0.5));

        var error = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await executor.ExecuteAsync<int>(
                static (_, _) => ValueTask.FromException<int>(new InvalidOperationException("retry")),
                static _ => false,
                static _ => true));

        error.Message.ShouldBe("retry");
    }

    [Fact]
    public void Policy_and_schedule_reject_invalid_values()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new RetryPolicy { InitialDelay = TimeSpan.FromTicks(-1) });
        Should.Throw<ArgumentOutOfRangeException>(() => new RetryPolicy { MaximumAttempts = 0 });
        Should.Throw<ArgumentOutOfRangeException>(() => new RetryPolicy { MaximumDuration = TimeSpan.Zero });
        Should.Throw<ArgumentOutOfRangeException>(() => new RetryPolicy { JitterFactor = 1.1 });
        Should.Throw<ArgumentOutOfRangeException>(() => RetrySchedule.GetDelay(new RetryPolicy(), 0));
        Should.Throw<ArgumentOutOfRangeException>(() => RetrySchedule.GetDelay(new RetryPolicy(), 1, -0.1));
    }

    private sealed class FixedJitterSource(double sample) : IRetryJitterSource
    {
        public double NextSample() => sample;
    }
}
