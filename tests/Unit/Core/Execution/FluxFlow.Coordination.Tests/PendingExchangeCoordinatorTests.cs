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
    public async Task Infinite_exchange_remains_bounded_and_pending_while_finite_exchange_expires()
    {
        var time = new FakeTimeProvider();
        await using var coordinator = CreateCoordinator(maxPending: 2, timeProvider: time);
        var infinite = coordinator.TryStart("infinite", "context", Timeout.InfiniteTimeSpan);
        var finite = coordinator.TryStart("finite", "context", TimeSpan.FromSeconds(1));
        coordinator.TryStart("full", "context").Status.ShouldBe(PendingExchangeStartStatus.CapacityReached);

        time.Advance(TimeSpan.FromDays(365));

        (await finite.Completion!).Kind.ShouldBe(PendingExchangeCompletionKind.TimedOut);
        infinite.Completion!.IsCompleted.ShouldBeFalse();
        coordinator.PendingCount.ShouldBe(1);
        coordinator.TryResolve("infinite", 42).Status.ShouldBe(PendingExchangeFeedbackStatus.Resolved);
        var result = await infinite.Completion;
        result.Outcome.ShouldBe(42);
        result.CompletedAt.ShouldBe(time.GetUtcNow());
        coordinator.PendingCount.ShouldBe(0);
    }

    [Fact]
    public async Task Deadline_compaction_keeps_infinite_entries_cancellable_without_expiring_them()
    {
        var time = new FakeTimeProvider();
        await using var coordinator = CreateCoordinator(timeProvider: time);
        var infinite = coordinator.TryStart("infinite", "context", Timeout.InfiniteTimeSpan);
        var blocker = coordinator.TryStart("blocker", "context", TimeSpan.FromDays(1));
        for (var index = 0; index < 100; index++)
        {
            var key = $"finite-{index}";
            var pending = coordinator.TryStart(key, "context", TimeSpan.FromDays(2));
            coordinator.TryResolve(key, index).Status.ShouldBe(PendingExchangeFeedbackStatus.Resolved);
            (await pending.Completion!).Outcome.ShouldBe(index);
        }

        time.Advance(TimeSpan.FromDays(3));

        (await blocker.Completion!).Kind.ShouldBe(PendingExchangeCompletionKind.TimedOut);
        infinite.Completion!.IsCompleted.ShouldBeFalse();
        coordinator.PendingCount.ShouldBe(1);
        var cancellation = new OperationCanceledException("Cancel infinite exchange.");
        coordinator.TryCancel("infinite", cancellation);
        var result = await infinite.Completion;
        result.Kind.ShouldBe(PendingExchangeCompletionKind.Cancelled);
        result.Error.ShouldBeSameAs(cancellation);
        coordinator.PendingCount.ShouldBe(0);
    }

    [Fact]
    public async Task Reply_before_deadline_resolves_without_waiting_for_timer_callback()
    {
        var time = new DelayedCallbackTimeProvider();
        await using var coordinator = CreateCoordinator(timeProvider: time);
        var started = coordinator.TryStart("first", "context", TimeSpan.FromSeconds(1));
        time.Advance(TimeSpan.FromMilliseconds(999));

        var feedback = coordinator.TryResolve("first", 42);
        var completion = await started.Completion!;

        feedback.Status.ShouldBe(PendingExchangeFeedbackStatus.Resolved);
        completion.Kind.ShouldBe(PendingExchangeCompletionKind.Resolved);
        completion.Outcome.ShouldBe(42);
        coordinator.PendingCount.ShouldBe(0);
        time.Advance(TimeSpan.FromMilliseconds(1));
        time.RunCallbacks();
        (await started.Completion!).ShouldBeSameAs(completion);
    }

    [Theory]
    [InlineData(1000)]
    [InlineData(1001)]
    public async Task Reply_at_or_after_deadline_is_late_even_before_timer_callback_runs(int elapsedMilliseconds)
    {
        var time = new DelayedCallbackTimeProvider();
        await using var coordinator = CreateCoordinator(maxPending: 1, timeProvider: time);
        var started = coordinator.TryStart("first", "context", TimeSpan.FromSeconds(1));
        time.Advance(TimeSpan.FromMilliseconds(elapsedMilliseconds));
        started.Completion!.IsCompleted.ShouldBeFalse();

        var feedback = coordinator.TryResolve("first", 42);
        var completion = await started.Completion!;

        feedback.Status.ShouldBe(PendingExchangeFeedbackStatus.Late);
        completion.Kind.ShouldBe(PendingExchangeCompletionKind.TimedOut);
        completion.Outcome.ShouldBe(0);
        completion.Error.ShouldBeNull();
        completion.Context.ShouldBe("context");
        completion.CompletedAt.ShouldBe(time.GetUtcNow());
        coordinator.PendingCount.ShouldBe(0);
        var next = coordinator.TryStart("next", "next context", TimeSpan.FromSeconds(2));
        next.Status.ShouldBe(PendingExchangeStartStatus.Accepted);

        time.RunCallbacks();

        (await started.Completion!).ShouldBeSameAs(completion);
        coordinator.TryResolve("first", 43).Status.ShouldBe(PendingExchangeFeedbackStatus.Late);
        next.Completion!.IsCompleted.ShouldBeFalse();
        coordinator.PendingCount.ShouldBe(1);
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Stop_drains_exchanges_before_timer_disarm_failure(bool faultedStop)
    {
        var time = new FaultingTimeProvider();
        await using var coordinator = CreateCoordinator(maxPending: 2, timeProvider: time);
        var finite = coordinator.TryStart("finite", "first context");
        var infinite = coordinator.TryStart("infinite", "second context", Timeout.InfiniteTimeSpan);
        var timerError = new InvalidOperationException("Timer disarm failed.");
        var stopError = faultedStop ? new Exception("Application failed.") : null;
        time.DisableError = timerError;

        Should.Throw<InvalidOperationException>(() => coordinator.Stop(stopError)).ShouldBeSameAs(timerError);

        coordinator.PendingCount.ShouldBe(0);
        coordinator.IsStopped.ShouldBeTrue();
        coordinator.TryStart("next", "not admitted").Status.ShouldBe(PendingExchangeStartStatus.Stopped);
        finite.Completion!.IsCompletedSuccessfully.ShouldBeTrue();
        infinite.Completion!.IsCompletedSuccessfully.ShouldBeTrue();
        var completions = await Task.WhenAll(finite.Completion, infinite.Completion);
        completions.Select(completion => (completion.Key, completion.Context))
            .ShouldBe([("finite", "first context"), ("infinite", "second context")]);
        foreach (var completion in completions)
        {
            completion.Kind.ShouldBe(faultedStop ? PendingExchangeCompletionKind.Faulted : PendingExchangeCompletionKind.Stopped);
            completion.Error.ShouldBeSameAs(stopError);
            completion.Outcome.ShouldBe(0);
            completion.CompletedAt.ShouldBe(time.GetUtcNow());
        }

        coordinator.Stop().ShouldBeEmpty();
        time.RunLateCallback();
        time.DisableCalls.ShouldBe(1);
        coordinator.TryResolve("finite", 42).Status.ShouldBe(PendingExchangeFeedbackStatus.Stopped);
        (await finite.Completion).ShouldBeSameAs(completions[0]);
        (await infinite.Completion).ShouldBeSameAs(completions[1]);
    }

    [Fact]
    public async Task Fault_all_drains_before_timer_disarm_failure_and_can_resume()
    {
        var time = new FaultingTimeProvider();
        await using var coordinator = CreateCoordinator(maxPending: 2, timeProvider: time);
        var finite = coordinator.TryStart("finite", "first context");
        var infinite = coordinator.TryStart("infinite", "second context", Timeout.InfiniteTimeSpan);
        var failure = new Exception("Exchange failed.");
        var timerError = new InvalidOperationException("Timer disarm failed.");
        time.DisableError = timerError;

        try
        {
            Should.Throw<InvalidOperationException>(() => coordinator.FaultAll(failure)).ShouldBeSameAs(timerError);

            coordinator.PendingCount.ShouldBe(0);
            coordinator.IsStopped.ShouldBeFalse();
            finite.Completion!.IsCompletedSuccessfully.ShouldBeTrue();
            infinite.Completion!.IsCompletedSuccessfully.ShouldBeTrue();
            var completions = await Task.WhenAll(finite.Completion, infinite.Completion);
            completions.Select(completion => (completion.Key, completion.Context))
                .ShouldBe([("finite", "first context"), ("infinite", "second context")]);
            completions.ShouldAllBe(completion => completion.Kind == PendingExchangeCompletionKind.Faulted);
            foreach (var completion in completions) completion.Error.ShouldBeSameAs(failure);
            coordinator.TryResolve("finite", 1).Status.ShouldBe(PendingExchangeFeedbackStatus.Late);
            coordinator.TryStart("infinite", "duplicate").Status.ShouldBe(PendingExchangeStartStatus.Duplicate);

            time.DisableError = null;
            var next = coordinator.TryStart("next", "next context", TimeSpan.FromSeconds(1));
            next.Status.ShouldBe(PendingExchangeStartStatus.Accepted);
            time.RunLateCallback();
            next.Completion!.IsCompleted.ShouldBeFalse();
            time.Advance(TimeSpan.FromSeconds(1));
            (await next.Completion.WaitAsync(TimeSpan.FromSeconds(5))).Kind.ShouldBe(PendingExchangeCompletionKind.TimedOut);
            coordinator.PendingCount.ShouldBe(0);
            (await finite.Completion).ShouldBeSameAs(completions[0]);
            (await infinite.Completion).ShouldBeSameAs(completions[1]);
        }
        finally
        {
            time.DisableError = null;
        }
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Dispose_settles_and_disposes_timer_despite_cleanup_failures(bool failStop, bool failDispose)
    {
        var time = new FaultingTimeProvider();
        var coordinator = CreateCoordinator(maxPending: 2, timeProvider: time);
        var finite = coordinator.TryStart("finite", "first context");
        var infinite = coordinator.TryStart("infinite", "second context", Timeout.InfiniteTimeSpan);
        var stopError = new InvalidOperationException("Timer disarm failed.");
        var disposeError = new InvalidOperationException("Timer disposal failed.");
        time.DisableError = failStop ? stopError : null;
        time.DisposeError = failDispose ? disposeError : null;

        if (failStop && failDispose)
        {
            var error = await Should.ThrowAsync<AggregateException>(() => coordinator.DisposeAsync().AsTask());
            error.InnerExceptions.Count.ShouldBe(2);
            error.InnerExceptions[0].ShouldBeSameAs(stopError);
            error.InnerExceptions[1].ShouldBeSameAs(disposeError);
        }
        else
        {
            var error = await Should.ThrowAsync<InvalidOperationException>(() => coordinator.DisposeAsync().AsTask());
            error.ShouldBeSameAs(failStop ? stopError : disposeError);
        }

        time.DisposeCalls.ShouldBe(1);
        time.DisableCalls.ShouldBe(1);
        coordinator.PendingCount.ShouldBe(0);
        coordinator.IsStopped.ShouldBeTrue();
        finite.Completion!.IsCompletedSuccessfully.ShouldBeTrue();
        infinite.Completion!.IsCompletedSuccessfully.ShouldBeTrue();
        var completions = await Task.WhenAll(finite.Completion, infinite.Completion);
        completions.ShouldAllBe(completion => completion.Kind == PendingExchangeCompletionKind.Stopped && completion.Error == null);

        await coordinator.DisposeAsync();
        time.DisposeCalls.ShouldBe(1);
        time.DisableCalls.ShouldBe(1);
        coordinator.TryStart("next", "not admitted").Status.ShouldBe(PendingExchangeStartStatus.Stopped);
    }

    [Fact]
    public async Task Dispose_waits_for_asynchronous_timer_cleanup()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var time = new FaultingTimeProvider { DisposalGate = release.Task };
        var coordinator = CreateCoordinator(timeProvider: time);
        var started = coordinator.TryStart("pending", "context");

        var disposing = coordinator.DisposeAsync().AsTask();
        try
        {
            time.DisposeCalls.ShouldBe(1);
            disposing.IsCompleted.ShouldBeFalse();
            started.Completion!.IsCompletedSuccessfully.ShouldBeTrue();
            (await started.Completion).Kind.ShouldBe(PendingExchangeCompletionKind.Stopped);
            coordinator.PendingCount.ShouldBe(0);
        }
        finally
        {
            release.TrySetResult();
            await disposing.WaitAsync(TimeSpan.FromSeconds(5));
        }

        disposing.IsCompletedSuccessfully.ShouldBeTrue();
        await coordinator.DisposeAsync();
        time.DisposeCalls.ShouldBe(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_start_faults_existing_exchanges_and_stops_admission(bool declineChange)
    {
        var time = new FaultingTimeProvider();
        var coordinator = CreateCoordinator(maxPending: 3, timeProvider: time);
        var finite = coordinator.TryStart("finite", "first");
        var infinite = coordinator.TryStart("infinite", "second", Timeout.InfiniteTimeSpan);
        var failure = new InvalidOperationException("Scheduling failed.");
        time.ChangeError = declineChange ? null : failure;
        time.DeclineChange = declineChange;

        var error = Should.Throw<InvalidOperationException>(() => coordinator.TryStart("unreturned", "third"));

        if (declineChange) error.Message.ShouldBe("The pending exchange timer rejected scheduling.");
        else error.ShouldBeSameAs(failure);
        coordinator.IsStopped.ShouldBeTrue();
        coordinator.PendingCount.ShouldBe(0);
        finite.Completion!.IsCompletedSuccessfully.ShouldBeTrue();
        infinite.Completion!.IsCompletedSuccessfully.ShouldBeTrue();
        var completed = await Task.WhenAll(finite.Completion, infinite.Completion);
        completed.Select(item => (item.Key, item.Context)).ShouldBe([("finite", "first"), ("infinite", "second")]);
        completed.ShouldAllBe(item => item.Kind == PendingExchangeCompletionKind.Faulted);
        foreach (var item in completed) item.Error.ShouldBeSameAs(error);
        var changes = time.ChangeCalls;
        coordinator.TryStart("next", "rejected").Status.ShouldBe(PendingExchangeStartStatus.Stopped);
        coordinator.TryResolve("finite", 1).Status.ShouldBe(PendingExchangeFeedbackStatus.Stopped);
        coordinator.Stop().ShouldBeEmpty();
        time.RunLateCallback();
        time.ChangeCalls.ShouldBe(changes);

        (await Should.ThrowAsync<InvalidOperationException>(() => coordinator.DisposeAsync().AsTask())).ShouldBeSameAs(error);
        await coordinator.DisposeAsync();
        time.DisposeCalls.ShouldBe(1);
    }

    [Theory]
    [InlineData(PendingExchangeCompletionKind.Resolved)]
    [InlineData(PendingExchangeCompletionKind.Cancelled)]
    [InlineData(PendingExchangeCompletionKind.Faulted)]
    [InlineData(PendingExchangeCompletionKind.TimedOut)]
    public async Task Settlement_keeps_its_outcome_when_rescheduling_fails(PendingExchangeCompletionKind kind)
    {
        var time = new FaultingTimeProvider { DeferCallbacks = true };
        var coordinator = CreateCoordinator(maxPending: 3, timeProvider: time);
        var selected = coordinator.TryStart("selected", "selected context", TimeSpan.FromSeconds(1));
        var future = coordinator.TryStart("future", "future context", TimeSpan.FromSeconds(5));
        var infinite = coordinator.TryStart("infinite", "infinite context", Timeout.InfiniteTimeSpan);
        var domainError = new InvalidOperationException("Exchange failed.");
        var cancellation = new OperationCanceledException("Caller cancelled.");
        var failure = new InvalidOperationException("Rescheduling failed.");
        if (kind == PendingExchangeCompletionKind.TimedOut) time.Advance(TimeSpan.FromSeconds(1));
        time.ChangeError = failure;
        Func<PendingExchangeFeedback<string, string, int>> settle = kind switch
        {
            PendingExchangeCompletionKind.Cancelled => () => coordinator.TryCancel("selected", cancellation),
            PendingExchangeCompletionKind.Faulted => () => coordinator.TryFault("selected", domainError),
            _ => () => coordinator.TryResolve("selected", 42)
        };

        Should.Throw<InvalidOperationException>(() => settle()).ShouldBeSameAs(failure);

        selected.Completion!.IsCompletedSuccessfully.ShouldBeTrue();
        var completed = await selected.Completion;
        completed.Kind.ShouldBe(kind);
        completed.Key.ShouldBe("selected");
        completed.Context.ShouldBe("selected context");
        completed.Outcome.ShouldBe(kind == PendingExchangeCompletionKind.Resolved ? 42 : 0);
        completed.Error.ShouldBeSameAs(kind == PendingExchangeCompletionKind.Cancelled ? cancellation :
            kind == PendingExchangeCompletionKind.Faulted ? domainError : null);
        completed.CompletedAt.ShouldBe(time.GetUtcNow());
        future.Completion!.IsCompletedSuccessfully.ShouldBeTrue();
        infinite.Completion!.IsCompletedSuccessfully.ShouldBeTrue();
        foreach (var remaining in await Task.WhenAll(future.Completion, infinite.Completion))
        {
            remaining.Kind.ShouldBe(PendingExchangeCompletionKind.Faulted);
            remaining.Error.ShouldBeSameAs(failure);
        }
        coordinator.PendingCount.ShouldBe(0);
        coordinator.IsStopped.ShouldBeTrue();
        time.RunLateCallback();
        (await selected.Completion).ShouldBeSameAs(completed);
        (await Should.ThrowAsync<InvalidOperationException>(() => coordinator.DisposeAsync().AsTask())).ShouldBeSameAs(failure);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Timer_callback_preserves_timeouts_and_faults_remaining_exchanges(bool futureDeadline, bool declineChange)
    {
        var time = new FaultingTimeProvider();
        var coordinator = CreateCoordinator(maxPending: 4, timeProvider: time);
        var due = coordinator.TryStart("due", "first", TimeSpan.FromSeconds(1));
        var alsoDue = coordinator.TryStart("also due", "second", TimeSpan.FromSeconds(1));
        var infinite = coordinator.TryStart("infinite", "third", Timeout.InfiniteTimeSpan);
        PendingExchangeStart<string, string, int>? future = futureDeadline
            ? coordinator.TryStart("future", "fourth", TimeSpan.FromSeconds(5)) : null;
        var failure = new InvalidOperationException("Callback scheduling failed.");
        time.ChangeError = declineChange ? null : failure;
        time.DeclineChange = declineChange;

        time.Advance(TimeSpan.FromSeconds(1));

        due.Completion!.IsCompletedSuccessfully.ShouldBeTrue();
        alsoDue.Completion!.IsCompletedSuccessfully.ShouldBeTrue();
        foreach (var expired in await Task.WhenAll(due.Completion, alsoDue.Completion))
        {
            expired.Kind.ShouldBe(PendingExchangeCompletionKind.TimedOut);
            expired.Error.ShouldBeNull();
            expired.CompletedAt.ShouldBe(time.GetUtcNow());
        }
        infinite.Completion!.IsCompletedSuccessfully.ShouldBeTrue();
        var failed = await infinite.Completion;
        failed.Kind.ShouldBe(PendingExchangeCompletionKind.Faulted);
        var error = failed.Error.ShouldBeOfType<InvalidOperationException>();
        if (declineChange) error.Message.ShouldBe("The pending exchange timer rejected scheduling.");
        else error.ShouldBeSameAs(failure);
        if (future is { } scheduled)
        {
            scheduled.Completion!.IsCompletedSuccessfully.ShouldBeTrue();
            (await scheduled.Completion).Kind.ShouldBe(PendingExchangeCompletionKind.Faulted);
            (await scheduled.Completion).Error.ShouldBeSameAs(error);
        }
        coordinator.PendingCount.ShouldBe(0);
        coordinator.IsStopped.ShouldBeTrue();
        var changes = time.ChangeCalls;
        time.RunLateCallback();
        time.ChangeCalls.ShouldBe(changes);
        (await Should.ThrowAsync<InvalidOperationException>(() => coordinator.DisposeAsync().AsTask())).ShouldBeSameAs(error);
        time.DisposeCalls.ShouldBe(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Callback_failure_is_reported_on_dispose_after_all_exchanges_expire(bool failDispose)
    {
        var time = new FaultingTimeProvider();
        var coordinator = CreateCoordinator(timeProvider: time);
        var first = coordinator.TryStart("first", "one", TimeSpan.FromSeconds(1));
        var second = coordinator.TryStart("second", "two", TimeSpan.FromSeconds(1));
        var failure = new InvalidOperationException("Callback disarm failed.");
        var disposalFailure = new InvalidOperationException("Timer disposal failed.");
        time.DisableError = failure;
        time.DisposeError = failDispose ? disposalFailure : null;

        time.Advance(TimeSpan.FromSeconds(1));

        first.Completion!.IsCompletedSuccessfully.ShouldBeTrue();
        second.Completion!.IsCompletedSuccessfully.ShouldBeTrue();
        (await Task.WhenAll(first.Completion, second.Completion)).ShouldAllBe(item => item.Kind == PendingExchangeCompletionKind.TimedOut);
        coordinator.PendingCount.ShouldBe(0);
        coordinator.IsStopped.ShouldBeTrue();
        if (failDispose)
        {
            var error = await Should.ThrowAsync<AggregateException>(() => coordinator.DisposeAsync().AsTask());
            error.InnerExceptions.ShouldBe([failure, disposalFailure]);
        }
        else
            (await Should.ThrowAsync<InvalidOperationException>(() => coordinator.DisposeAsync().AsTask())).ShouldBeSameAs(failure);
        await coordinator.DisposeAsync();
        time.DisposeCalls.ShouldBe(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Settlement_does_not_run_consumer_continuations_inline(bool timer)
    {
        var time = new FakeTimeProvider();
        await using var coordinator = CreateCoordinator(timeProvider: time);
        var started = coordinator.TryStart("first", "context", TimeSpan.FromSeconds(1));
        using var insideOperation = new ThreadLocal<bool>();
        var continuation = started.Completion!.ContinueWith(_ => insideOperation.Value,
            CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

        insideOperation.Value = true;
        try
        {
            if (timer) time.Advance(TimeSpan.FromSeconds(1));
            else coordinator.TryResolve("first", 42);
        }
        finally { insideOperation.Value = false; }

        (await continuation.WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeFalse();
        (await started.Completion).Kind.ShouldBe(timer ? PendingExchangeCompletionKind.TimedOut : PendingExchangeCompletionKind.Resolved);
        coordinator.PendingCount.ShouldBe(0);
    }

    private sealed class FaultingTimeProvider : FakeTimeProvider
    {
        private Action? _callback;
        public Exception? DisableError { get; set; }
        public Exception? ChangeError { get; set; }
        public bool DeclineChange { get; set; }
        public bool DeferCallbacks { get; init; }
        public Exception? DisposeError { get; set; }
        public Task? DisposalGate { get; init; }
        public int DisableCalls { get; private set; }
        public int ChangeCalls { get; private set; }
        public int DisposeCalls { get; private set; }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            _callback = () => callback(state);
            return new FaultingTimer(this, base.CreateTimer(value =>
            {
                if (!DeferCallbacks) callback(value);
            }, state, dueTime, period));
        }

        public void RunLateCallback() => _callback!();

        private sealed class FaultingTimer(FaultingTimeProvider owner, ITimer inner) : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                owner.ChangeCalls++;
                if (owner.ChangeError is { } failure) throw failure;
                if (owner.DeclineChange) return false;
                if (dueTime == Timeout.InfiniteTimeSpan)
                {
                    owner.DisableCalls++;
                    if (owner.DisableError is { } error) throw error;
                }
                return inner.Change(dueTime, period);
            }

            public void Dispose() => inner.Dispose();

            public async ValueTask DisposeAsync()
            {
                owner.DisposeCalls++;
                if (owner.DisposalGate is { } gate) await gate;
                await inner.DisposeAsync();
                if (owner.DisposeError is { } error) throw error;
            }
        }
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

    private sealed class DelayedCallbackTimeProvider : FakeTimeProvider
    {
        private readonly Queue<Action> _callbacks = [];

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
            => base.CreateTimer(value => _callbacks.Enqueue(() => callback(value)), state, dueTime, period);

        public void RunCallbacks()
        {
            while (_callbacks.TryDequeue(out var callback)) callback();
        }
    }

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
