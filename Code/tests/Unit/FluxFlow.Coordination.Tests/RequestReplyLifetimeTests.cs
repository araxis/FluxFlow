using System.Text.Json;
using FluxFlow.Core.Coordination;
using FluxFlow.Core.Messaging;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;

namespace FluxFlow.Coordination.Tests;

public sealed class RequestReplyLifetimeTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Send_and_stop_preserve_exception_identity_and_task_outcome(bool stop, bool canceled)
    {
        await using var requests = new FlowRequestReply(capacity: 1);
        using var cancellation = new CancellationTokenSource();
        Exception failure = canceled
            ? new OperationCanceledException("operation canceled", cancellation.Token)
            : new AggregateException(new InvalidOperationException("operation failed"));
        var task = requests.RequestAsync("reply", FlowMessage.Create((FlowValue)"input"),
            (_, _) => stop ? ValueTask.CompletedTask : ValueTask.FromException(failure), Wait);
        if (stop) requests.Stop(failure);

        (await Record.ExceptionAsync(() => task.WaitAsync(Wait))).ShouldBeSameAs(failure);
        task.IsCanceled.ShouldBe(canceled);
        task.IsFaulted.ShouldBe(!canceled);
        if (canceled) ((OperationCanceledException)failure).CancellationToken.ShouldBe(cancellation.Token);
        requests.PendingCount.ShouldBe(0);
    }

    [Fact]
    public async Task Failed_scheduling_during_add_releases_existing_and_new_requests()
    {
        var clock = new FaultingTimeProvider();
        var requests = new FlowRequestReply(timeProvider: clock);
        var existing = Start(requests, Timeout.InfiniteTimeSpan);
        var failure = new InvalidOperationException("timer scheduling failed");
        clock.ChangeError = failure;
        var added = Start(requests);
        added.Sent.ShouldBeNull();
        foreach (var task in new[] { existing.Task, added.Task })
            (await Record.ExceptionAsync(() => task.WaitAsync(Wait))).ShouldBeSameAs(failure);
        requests.PendingCount.ShouldBe(0);
        (await Record.ExceptionAsync(() => requests.DisposeAsync().AsTask())).ShouldBeSameAs(failure);
        clock.DisposeCalls.ShouldBe(1);
    }

    private static (Task<FlowMessage> Task, FlowMessage Sent) Start(FlowRequestReply requests,
        TimeSpan? timeout = null, CancellationToken cancellation = default)
    {
        FlowMessage? sent = null;
        var task = requests.RequestAsync("reply", FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(1))),
            (message, _) => { sent = message; return ValueTask.CompletedTask; }, timeout ?? Wait, cancellation);
        return (task, sent!);
    }

    [Theory]
    [InlineData(999)]
    [InlineData(1000)]
    [InlineData(1001)]
    public async Task Reply_obeys_deadline_even_when_timer_callback_is_delayed(int elapsed)
    {
        var clock = new DelayedCallbackTimeProvider();
        await using var requests = new FlowRequestReply(timeProvider: clock);
        var pending = Start(requests, TimeSpan.FromSeconds(1));
        clock.Advance(TimeSpan.FromMilliseconds(elapsed));
        pending.Task.IsCompleted.ShouldBeFalse();
        requests.Reply("reply", pending.Sent).ShouldBe(elapsed < 1000);
        if (elapsed < 1000) (await pending.Task.WaitAsync(Wait)).ShouldBeSameAs(pending.Sent);
        else await Should.ThrowAsync<TimeoutException>(() => pending.Task.WaitAsync(Wait));
        var next = Start(requests);
        clock.RunCallbacks();
        requests.Reply("reply", pending.Sent).ShouldBeFalse();
        next.Task.IsCompleted.ShouldBeFalse();
        requests.Reply("reply", next.Sent).ShouldBeTrue();
        await next.Task.WaitAsync(Wait);
        requests.PendingCount.ShouldBe(0);
    }

    [Fact]
    public async Task Capacity_counts_infinite_requests_and_is_released_on_expiry()
    {
        var clock = new FakeTimeProvider();
        await using var requests = new FlowRequestReply(capacity: 2, timeProvider: clock);
        var infinite = Start(requests, Timeout.InfiniteTimeSpan);
        var finite = Start(requests, TimeSpan.FromSeconds(1));
        var rejected = Start(requests);
        await Should.ThrowAsync<InvalidOperationException>(() => rejected.Task.WaitAsync(Wait));
        rejected.Sent.ShouldBeNull();
        clock.Advance(TimeSpan.FromDays(365));
        await Should.ThrowAsync<TimeoutException>(() => finite.Task.WaitAsync(Wait));
        infinite.Task.IsCompleted.ShouldBeFalse();
        requests.PendingCount.ShouldBe(1);
        var next = Start(requests);
        requests.Reply("reply", finite.Sent).ShouldBeFalse();
        next.Task.IsCompleted.ShouldBeFalse();
        requests.Reply("reply", next.Sent).ShouldBeTrue();
        requests.Reply("reply", infinite.Sent).ShouldBeTrue();
        await Task.WhenAll(next.Task, infinite.Task).WaitAsync(Wait);
    }

    [Fact]
    public async Task Many_completed_deadlines_do_not_expire_an_infinite_request()
    {
        var clock = new FakeTimeProvider();
        await using var requests = new FlowRequestReply(timeProvider: clock);
        using var cancellation = new CancellationTokenSource();
        var infinite = Start(requests, Timeout.InfiniteTimeSpan, cancellation.Token);
        var blocker = Start(requests, TimeSpan.FromDays(1));
        for (var index = 0; index < 100; index++)
        {
            var pending = Start(requests, TimeSpan.FromDays(2));
            requests.Reply("reply", pending.Sent).ShouldBeTrue();
            await pending.Task.WaitAsync(Wait);
        }
        clock.Advance(TimeSpan.FromDays(3));
        await Should.ThrowAsync<TimeoutException>(() => blocker.Task.WaitAsync(Wait));
        infinite.Task.IsCompleted.ShouldBeFalse();
        cancellation.Cancel();
        var error = await Should.ThrowAsync<OperationCanceledException>(() => infinite.Task.WaitAsync(Wait));
        error.CancellationToken.ShouldBe(cancellation.Token);
        requests.Reply("reply", infinite.Sent).ShouldBeFalse();
        requests.PendingCount.ShouldBe(0);
    }

    [Fact]
    public async Task Reply_cancellation_and_timeout_race_has_one_terminal_result()
    {
        for (var index = 0; index < 100; index++)
        {
            var clock = new FakeTimeProvider();
            await using var requests = new FlowRequestReply(timeProvider: clock);
            using var cancellation = new CancellationTokenSource();
            var pending = Start(requests, TimeSpan.FromSeconds(1), cancellation.Token);
            var replied = false;
            await Task.WhenAll(
                Task.Run(() => replied = requests.Reply("reply", pending.Sent)),
                Task.Run(() => cancellation.Cancel()),
                Task.Run(() => clock.Advance(TimeSpan.FromSeconds(1))));
            var error = await Record.ExceptionAsync(() => pending.Task.WaitAsync(Wait));
            if (replied) { error.ShouldBeNull(); (await pending.Task).ShouldBeSameAs(pending.Sent); }
            else (error is OperationCanceledException or TimeoutException).ShouldBeTrue();
            requests.Reply("reply", pending.Sent).ShouldBeFalse();
            requests.PendingCount.ShouldBe(0);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Concurrent_start_and_shutdown_leave_no_waiter_unsettled(bool dispose)
    {
        await using var requests = new FlowRequestReply(capacity: 256);
        var tasks = new Task<FlowMessage>[256];
        await Task.WhenAll(Task.Run(() =>
        {
            for (var index = 0; index < tasks.Length; index++) tasks[index] = Start(requests).Task;
        }), Task.Run(async () =>
        {
            if (dispose) await requests.DisposeAsync();
            else requests.Stop();
        }));
        foreach (var task in tasks) await Should.ThrowAsync<OperationCanceledException>(() => task.WaitAsync(Wait));
        requests.PendingCount.ShouldBe(0);
    }

    [Fact]
    public async Task Timeout_and_disposal_race_releases_request()
    {
        var clock = new FakeTimeProvider();
        await using var requests = new FlowRequestReply(timeProvider: clock);
        var pending = Start(requests, TimeSpan.FromSeconds(1));
        await Task.WhenAll(Task.Run(() => clock.Advance(TimeSpan.FromSeconds(1))),
            Task.Run(async () => await requests.DisposeAsync()));
        var error = await Record.ExceptionAsync(() => pending.Task.WaitAsync(Wait));
        (error is TimeoutException or OperationCanceledException).ShouldBeTrue();
        requests.Reply("reply", pending.Sent).ShouldBeFalse();
        requests.PendingCount.ShouldBe(0);
    }

    [Fact]
    public async Task One_timer_serves_many_deadlines_including_long_timeouts()
    {
        var clock = new CountingFakeTimeProvider();
        await using var requests = new FlowRequestReply(timeProvider: clock);
        var tasks = Enumerable.Range(1, 100).Select(days => Start(requests, TimeSpan.FromDays(days)).Task).ToArray();
        clock.TimerCount.ShouldBe(1);
        clock.Advance(TimeSpan.FromDays(100));
        foreach (var task in tasks) await Should.ThrowAsync<TimeoutException>(() => task.WaitAsync(Wait));
        requests.PendingCount.ShouldBe(0);
    }

    [Fact]
    public async Task Invalid_capacity_deadlines_and_precancellation_do_not_admit_requests()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new FlowRequestReply(0));
        await using var requests = new FlowRequestReply();
        foreach (var timeout in new[] { TimeSpan.Zero, TimeSpan.FromSeconds(-2), TimeSpan.MaxValue })
        {
            var pending = Start(requests, timeout);
            await Should.ThrowAsync<ArgumentOutOfRangeException>(() => pending.Task);
            pending.Sent.ShouldBeNull();
        }
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancelled = Start(requests, cancellation: cancellation.Token);
        await Should.ThrowAsync<OperationCanceledException>(() => cancelled.Task);
        cancelled.Sent.ShouldBeNull();
        requests.PendingCount.ShouldBe(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Stop_drains_before_timer_disarm_failure(bool faultedStop)
    {
        var clock = new FaultingTimeProvider();
        await using var requests = new FlowRequestReply(timeProvider: clock);
        var finite = Start(requests);
        var infinite = Start(requests, Timeout.InfiniteTimeSpan);
        var timerError = new InvalidOperationException("Timer disarm failed.");
        var stopError = faultedStop ? new Exception("Component stopped with an error.") : null;
        clock.DisableError = timerError;
        Should.Throw<InvalidOperationException>(() => requests.Stop(stopError)).ShouldBeSameAs(timerError);
        foreach (var task in new[] { finite.Task, infinite.Task })
        {
            var error = await Record.ExceptionAsync(() => task.WaitAsync(Wait));
            if (faultedStop) error.ShouldBeSameAs(stopError);
            else error.ShouldBeOfType<OperationCanceledException>();
        }
        requests.Stop();
        clock.RunLateCallback();
        clock.DisableCalls.ShouldBe(1);
        requests.PendingCount.ShouldBe(0);
        await Should.ThrowAsync<OperationCanceledException>(() => Start(requests).Task);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Dispose_drains_and_releases_timer_despite_cleanup_failures(bool failStop, bool failDispose)
    {
        var clock = new FaultingTimeProvider();
        var requests = new FlowRequestReply(timeProvider: clock);
        var pending = Start(requests);
        var stopError = new InvalidOperationException("Stop failed.");
        var disposeError = new InvalidOperationException("Dispose failed.");
        clock.DisableError = failStop ? stopError : null;
        clock.DisposeError = failDispose ? disposeError : null;
        var error = await Record.ExceptionAsync(() => requests.DisposeAsync().AsTask());
        if (failStop && failDispose) error.ShouldBeOfType<AggregateException>().InnerExceptions.ShouldBe([stopError, disposeError]);
        else error.ShouldBeSameAs(failStop ? stopError : disposeError);
        await Should.ThrowAsync<OperationCanceledException>(() => pending.Task.WaitAsync(Wait));
        await requests.DisposeAsync();
        clock.DisposeCalls.ShouldBe(1);
        requests.PendingCount.ShouldBe(0);
    }

    [Fact]
    public async Task Dispose_waits_for_asynchronous_timer_cleanup()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var clock = new FaultingTimeProvider { DisposalGate = gate.Task };
        var requests = new FlowRequestReply(timeProvider: clock);
        var pending = Start(requests);
        var disposal = requests.DisposeAsync().AsTask();
        try
        {
            await Should.ThrowAsync<OperationCanceledException>(() => pending.Task.WaitAsync(Wait));
            disposal.IsCompleted.ShouldBeFalse();
        }
        finally { gate.TrySetResult(); await disposal.WaitAsync(Wait); }
        clock.DisposeCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Rejected_timer_scheduling_faults_waiters_without_admitting_another_send()
    {
        var clock = new FaultingTimeProvider();
        var requests = new FlowRequestReply(timeProvider: clock);
        var first = Start(requests);
        clock.DeclineChange = true;
        var rejected = Start(requests);
        var error = await Should.ThrowAsync<InvalidOperationException>(() => rejected.Task.WaitAsync(Wait));
        rejected.Sent.ShouldBeNull();
        (await Should.ThrowAsync<InvalidOperationException>(() => first.Task.WaitAsync(Wait))).ShouldBeSameAs(error);
        requests.PendingCount.ShouldBe(0);
        await Should.ThrowAsync<OperationCanceledException>(() => Start(requests).Task);
        (await Should.ThrowAsync<InvalidOperationException>(() => requests.DisposeAsync().AsTask())).ShouldBeSameAs(error);
    }

    [Fact]
    public async Task Reply_result_survives_rescheduling_failure_while_other_requests_fail()
    {
        var clock = new FaultingTimeProvider();
        var requests = new FlowRequestReply(timeProvider: clock);
        var selected = Start(requests);
        var other = Start(requests, Timeout.InfiniteTimeSpan);
        var failure = new InvalidOperationException("Scheduling failed.");
        clock.ChangeError = failure;
        Should.Throw<InvalidOperationException>(() => requests.Reply("reply", selected.Sent)).ShouldBeSameAs(failure);
        (await selected.Task.WaitAsync(Wait)).ShouldBeSameAs(selected.Sent);
        (await Should.ThrowAsync<InvalidOperationException>(() => other.Task.WaitAsync(Wait))).ShouldBeSameAs(failure);
        (await Should.ThrowAsync<InvalidOperationException>(() => requests.DisposeAsync().AsTask())).ShouldBeSameAs(failure);
        requests.PendingCount.ShouldBe(0);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Timer_failure_preserves_due_timeouts_and_faults_remaining_requests(bool futureDeadline, bool declineChange)
    {
        var clock = new FaultingTimeProvider();
        var requests = new FlowRequestReply(timeProvider: clock);
        var due = Start(requests, TimeSpan.FromSeconds(1));
        var alsoDue = Start(requests, TimeSpan.FromSeconds(1));
        var other = Start(requests, futureDeadline ? TimeSpan.FromSeconds(5) : Timeout.InfiniteTimeSpan);
        var failure = new InvalidOperationException("Scheduling failed.");
        clock.ChangeError = declineChange ? null : failure;
        clock.DeclineChange = declineChange;
        clock.Advance(TimeSpan.FromSeconds(1));
        await Should.ThrowAsync<TimeoutException>(() => due.Task.WaitAsync(Wait));
        await Should.ThrowAsync<TimeoutException>(() => alsoDue.Task.WaitAsync(Wait));
        var observed = await Should.ThrowAsync<InvalidOperationException>(() => other.Task.WaitAsync(Wait));
        if (!declineChange) observed.ShouldBeSameAs(failure);
        var changes = clock.ChangeCalls;
        clock.RunLateCallback();
        clock.ChangeCalls.ShouldBe(changes);
        (await Should.ThrowAsync<InvalidOperationException>(() => requests.DisposeAsync().AsTask())).ShouldBeSameAs(observed);
        requests.PendingCount.ShouldBe(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Callback_cleanup_failure_is_reported_even_after_all_requests_expire(bool failDispose)
    {
        var clock = new FaultingTimeProvider();
        var requests = new FlowRequestReply(timeProvider: clock);
        var pending = Start(requests, TimeSpan.FromSeconds(1));
        var failure = new InvalidOperationException("Disarm failed.");
        var disposalFailure = new InvalidOperationException("Dispose failed.");
        clock.DisableError = failure;
        clock.DisposeError = failDispose ? disposalFailure : null;
        clock.Advance(TimeSpan.FromSeconds(1));
        await Should.ThrowAsync<TimeoutException>(() => pending.Task.WaitAsync(Wait));
        var error = await Record.ExceptionAsync(() => requests.DisposeAsync().AsTask());
        if (failDispose) error.ShouldBeOfType<AggregateException>().InnerExceptions.ShouldBe([failure, disposalFailure]);
        else error.ShouldBeSameAs(failure);
        await requests.DisposeAsync();
        clock.DisposeCalls.ShouldBe(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reply_and_timeout_do_not_run_consumer_continuations_inline(bool timer)
    {
        var clock = new FakeTimeProvider();
        await using var requests = new FlowRequestReply(timeProvider: clock);
        var pending = Start(requests, TimeSpan.FromSeconds(1));
        using var insideOperation = new ThreadLocal<bool>();
        var continuation = pending.Task.ContinueWith(_ => insideOperation.Value,
            CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        insideOperation.Value = true;
        try
        {
            if (timer) clock.Advance(TimeSpan.FromSeconds(1));
            else requests.Reply("reply", pending.Sent);
        }
        finally { insideOperation.Value = false; }
        (await continuation.WaitAsync(Wait)).ShouldBeFalse();
        if (timer) await Should.ThrowAsync<TimeoutException>(() => pending.Task);
        else await pending.Task;
    }

    private sealed class FaultingTimeProvider : FakeTimeProvider
    {
        private Action? _callback;
        public Exception? DisableError { get; set; }
        public Exception? ChangeError { get; set; }
        public bool DeclineChange { get; set; }
        public Exception? DisposeError { get; set; }
        public Task? DisposalGate { get; init; }
        public int DisableCalls { get; private set; }
        public int ChangeCalls { get; private set; }
        public int DisposeCalls { get; private set; }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            _callback = () => callback(state);
            return new FaultingTimer(this, base.CreateTimer(callback, state, dueTime, period));
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
