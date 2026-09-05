using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;

namespace FluxFlow.Coordination.Tests;

public sealed class PendingExchangeCoordinatorTests
{
    [Fact]
    public async Task Accepted_exchange_resolves_once_and_preserves_context()
    {
        await using var coordinator = CreateCoordinator();

        var started = coordinator.TryStart("key", "context");
        var feedback = coordinator.TryResolve("key", 42);
        var completed = await started.Completion!;

        started.Status.ShouldBe(PendingExchangeStartStatus.Accepted);
        feedback.Status.ShouldBe(PendingExchangeFeedbackStatus.Resolved);
        feedback.Completion.ShouldBeSameAs(completed);
        completed.Key.ShouldBe("key");
        completed.Context.ShouldBe("context");
        completed.Kind.ShouldBe(PendingExchangeCompletionKind.Resolved);
        completed.Outcome.ShouldBe(42);
        completed.Error.ShouldBeNull();
        coordinator.PendingCount.ShouldBe(0);
        coordinator.TryResolve("key", 43).Status.ShouldBe(PendingExchangeFeedbackStatus.Duplicate);
    }

    [Fact]
    public async Task Duplicate_capacity_and_stopped_starts_are_reported()
    {
        await using var coordinator = CreateCoordinator(maxPending: 1);

        var accepted = coordinator.TryStart("first", "context");

        coordinator.TryStart("first", "duplicate").Status.ShouldBe(PendingExchangeStartStatus.Duplicate);
        coordinator.TryStart("second", "context").Status.ShouldBe(PendingExchangeStartStatus.CapacityReached);
        coordinator.Stop();
        coordinator.TryStart("third", "context").Status.ShouldBe(PendingExchangeStartStatus.Stopped);
        (await accepted.Completion!).Kind.ShouldBe(PendingExchangeCompletionKind.Stopped);
    }

    [Fact]
    public async Task Fake_time_expires_only_due_exchanges_and_classifies_late_feedback()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await using var coordinator = CreateCoordinator(timeProvider: time);
        var first = coordinator.TryStart("first", "one", TimeSpan.FromSeconds(2));
        var second = coordinator.TryStart("second", "two", TimeSpan.FromSeconds(5));

        time.Advance(TimeSpan.FromSeconds(2));
        var timedOut = await first.Completion!;

        timedOut.Kind.ShouldBe(PendingExchangeCompletionKind.TimedOut);
        timedOut.CompletedAt.ShouldBe(time.GetUtcNow());
        coordinator.PendingCount.ShouldBe(1);
        coordinator.TryResolve("first", 1).Status.ShouldBe(PendingExchangeFeedbackStatus.Late);
        second.Completion!.IsCompleted.ShouldBeFalse();
    }

    [Fact]
    public async Task Cancellation_and_fault_are_terminal_outcomes()
    {
        await using var coordinator = CreateCoordinator();
        var cancelled = coordinator.TryStart("cancel", "one");
        var faulted = coordinator.TryStart("fault", "two");
        var error = new InvalidOperationException("failed");

        coordinator.TryCancel("cancel").Status.ShouldBe(PendingExchangeFeedbackStatus.Resolved);
        coordinator.TryFault("fault", error).Status.ShouldBe(PendingExchangeFeedbackStatus.Resolved);

        (await cancelled.Completion!).Kind.ShouldBe(PendingExchangeCompletionKind.Cancelled);
        var faultCompletion = await faulted.Completion!;
        faultCompletion.Kind.ShouldBe(PendingExchangeCompletionKind.Faulted);
        faultCompletion.Error.ShouldBeSameAs(error);
        coordinator.TryResolve("cancel", 1).Status.ShouldBe(PendingExchangeFeedbackStatus.Late);
        coordinator.TryResolve("missing", 1).Status.ShouldBe(PendingExchangeFeedbackStatus.NotFound);
    }

    [Fact]
    public async Task Stop_settles_pending_exchanges_in_acceptance_order()
    {
        await using var coordinator = CreateCoordinator();
        var first = coordinator.TryStart("first", "one");
        var second = coordinator.TryStart("second", "two");

        var stopped = coordinator.Stop();

        stopped.Select(static completion => completion.Key).ShouldBe(["first", "second"]);
        stopped.ShouldAllBe(static completion => completion.Kind == PendingExchangeCompletionKind.Stopped);
        (await first.Completion!).Kind.ShouldBe(PendingExchangeCompletionKind.Stopped);
        (await second.Completion!).Kind.ShouldBe(PendingExchangeCompletionKind.Stopped);
        coordinator.Stop().ShouldBeEmpty();
        coordinator.TryResolve("first", 1).Status.ShouldBe(PendingExchangeFeedbackStatus.Stopped);
    }

    [Fact]
    public async Task Faulted_stop_preserves_the_stop_error()
    {
        await using var coordinator = CreateCoordinator();
        var started = coordinator.TryStart("key", "context");
        var error = new InvalidOperationException("stop failed");

        coordinator.Stop(error).ShouldHaveSingleItem().Error.ShouldBeSameAs(error);
        var completed = await started.Completion!;

        completed.Kind.ShouldBe(PendingExchangeCompletionKind.Faulted);
        completed.Error.ShouldBeSameAs(error);
    }

    [Fact]
    public async Task Fault_all_drains_current_exchanges_without_stopping_new_starts()
    {
        await using var coordinator = CreateCoordinator();
        var current = coordinator.TryStart("current", "context");
        var error = new InvalidOperationException("failed");

        coordinator.FaultAll(error).ShouldHaveSingleItem().Error.ShouldBeSameAs(error);
        (await current.Completion!).Kind.ShouldBe(PendingExchangeCompletionKind.Faulted);

        var next = coordinator.TryStart("next", "context");
        next.Status.ShouldBe(PendingExchangeStartStatus.Accepted);
        coordinator.TryResolve("next", 1).Status.ShouldBe(PendingExchangeFeedbackStatus.Resolved);
    }

    [Fact]
    public async Task Resolve_cancel_and_timeout_races_settle_exactly_once()
    {
        for (var index = 0; index < 100; index++)
        {
            var time = new FakeTimeProvider();
            await using var coordinator = CreateCoordinator<int, int, string>(timeProvider: time);
            var started = coordinator.TryStart(index, index, TimeSpan.FromSeconds(1));

            await Task.WhenAll(
                Task.Run(() => coordinator.TryResolve(index, "resolved")),
                Task.Run(() => coordinator.TryCancel(index)),
                Task.Run(() => time.Advance(TimeSpan.FromSeconds(1))));

            var completed = await started.Completion!;
            Enum.IsDefined(completed.Kind).ShouldBeTrue();
            coordinator.PendingCount.ShouldBe(0);
            coordinator.TryResolve(index, "late").Status.ShouldBeOneOf(
                PendingExchangeFeedbackStatus.Duplicate,
                PendingExchangeFeedbackStatus.Late);
        }
    }

    [Fact]
    public async Task Concurrent_start_and_stop_leave_no_accepted_exchange_unsettled()
    {
        await using var coordinator = CreateCoordinator<int, int, int>(maxPending: 256);
        var starts = new PendingExchangeStart<int, int, int>[256];

        await Task.WhenAll(
            Task.Run(() =>
            {
                for (var index = 0; index < starts.Length; index++)
                    starts[index] = coordinator.TryStart(index, index);
            }),
            Task.Run(() => coordinator.Stop()));

        foreach (var started in starts.Where(static start => start.IsAccepted))
        {
            var completed = await started.Completion!.WaitAsync(TimeSpan.FromSeconds(1));
            completed.Kind.ShouldBe(PendingExchangeCompletionKind.Stopped);
        }

        coordinator.PendingCount.ShouldBe(0);
    }

    [Fact]
    public async Task Concurrent_start_and_dispose_leave_no_accepted_exchange_unsettled()
    {
        var coordinator = CreateCoordinator<int, int, int>(maxPending: 256);
        var starts = new PendingExchangeStart<int, int, int>[256];
        using var release = new ManualResetEventSlim();

        var starting = Task.Run(() =>
        {
            release.Wait();
            for (var index = 0; index < starts.Length; index++)
                starts[index] = coordinator.TryStart(index, index);
        });
        var disposing = Task.Run(async () =>
        {
            release.Wait();
            await coordinator.DisposeAsync();
        });

        release.Set();
        await Task.WhenAll(starting, disposing);

        foreach (var started in starts.Where(static start => start.IsAccepted))
        {
            var completed = await started.Completion!.WaitAsync(TimeSpan.FromSeconds(1));
            completed.Kind.ShouldBe(PendingExchangeCompletionKind.Stopped);
        }

        coordinator.PendingCount.ShouldBe(0);
        coordinator.IsStopped.ShouldBeTrue();
    }

    [Fact]
    public async Task Timeout_and_dispose_race_settles_once_and_cleans_pending_state()
    {
        var time = new FakeTimeProvider();
        var coordinator = CreateCoordinator<int, int, int>(timeProvider: time);
        var started = coordinator.TryStart(1, 1, TimeSpan.FromSeconds(1));
        using var release = new ManualResetEventSlim();

        var timingOut = Task.Run(() =>
        {
            release.Wait();
            time.Advance(TimeSpan.FromSeconds(1));
        });
        var disposing = Task.Run(async () =>
        {
            release.Wait();
            await coordinator.DisposeAsync();
        });

        release.Set();
        await Task.WhenAll(timingOut, disposing);

        var completed = await started.Completion!.WaitAsync(TimeSpan.FromSeconds(1));
        completed.Kind.ShouldBeOneOf(
            PendingExchangeCompletionKind.TimedOut,
            PendingExchangeCompletionKind.Stopped);
        coordinator.PendingCount.ShouldBe(0);
        coordinator.TryResolve(1, 2).Status.ShouldBe(PendingExchangeFeedbackStatus.Stopped);
    }

    [Fact]
    public async Task Unknown_feedback_is_not_found_until_the_coordinator_stops()
    {
        await using var coordinator = CreateCoordinator();

        coordinator.TryResolve("unknown", 1).Status.ShouldBe(PendingExchangeFeedbackStatus.NotFound);
        coordinator.TryCancel("unknown").Status.ShouldBe(PendingExchangeFeedbackStatus.NotFound);

        coordinator.Stop();

        coordinator.TryResolve("unknown", 1).Status.ShouldBe(PendingExchangeFeedbackStatus.Stopped);
    }

    [Fact]
    public async Task One_timer_coordinates_many_deadlines()
    {
        var time = new CountingFakeTimeProvider();
        await using var coordinator = CreateCoordinator<int, int, int>(maxPending: 100, timeProvider: time);

        for (var index = 0; index < 100; index++)
            coordinator.TryStart(index, index, TimeSpan.FromMinutes(index + 1));

        time.TimerCount.ShouldBe(1);
        coordinator.PendingCount.ShouldBe(100);
    }

    [Fact]
    public async Task Settled_history_is_bounded_and_prevents_immediate_key_reuse()
    {
        await using var coordinator = CreateCoordinator(settledKeyCapacity: 1);
        coordinator.TryStart("first", "context");
        coordinator.TryResolve("first", 1);

        coordinator.TryStart("first", "context").Status.ShouldBe(PendingExchangeStartStatus.Duplicate);

        coordinator.TryStart("second", "context");
        coordinator.TryResolve("second", 2);

        coordinator.TryResolve("first", 3).Status.ShouldBe(PendingExchangeFeedbackStatus.NotFound);
    }

    [Fact]
    public async Task Invalid_options_and_timeouts_are_rejected()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => CreateCoordinator(defaultTimeout: TimeSpan.Zero));
        Should.Throw<ArgumentOutOfRangeException>(() => CreateCoordinator(maxPending: 0));
        Should.Throw<ArgumentOutOfRangeException>(() => CreateCoordinator(settledKeyCapacity: 0));

        await using var coordinator = CreateCoordinator();
        Should.Throw<ArgumentOutOfRangeException>(() => coordinator.TryStart("key", "context", TimeSpan.Zero));
    }

    private static PendingExchangeCoordinator<TKey, TContext, TOutcome> CreateCoordinator<TKey, TContext, TOutcome>(
        int maxPending = 16,
        int settledKeyCapacity = 64,
        TimeSpan? defaultTimeout = null,
        TimeProvider? timeProvider = null)
        where TKey : notnull
        => new(
            new PendingExchangeCoordinatorOptions
            {
                DefaultTimeout = defaultTimeout ?? TimeSpan.FromSeconds(30),
                MaxPending = maxPending,
                SettledKeyCapacity = settledKeyCapacity
            },
            timeProvider);

    private static PendingExchangeCoordinator<string, string, int> CreateCoordinator(
        int maxPending = 16,
        int settledKeyCapacity = 64,
        TimeSpan? defaultTimeout = null,
        TimeProvider? timeProvider = null)
        => CreateCoordinator<string, string, int>(maxPending, settledKeyCapacity, defaultTimeout, timeProvider);

    private sealed class CountingFakeTimeProvider : FakeTimeProvider
    {
        public int TimerCount { get; private set; }

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            TimerCount++;
            return base.CreateTimer(callback, state, dueTime, period);
        }
    }
}
