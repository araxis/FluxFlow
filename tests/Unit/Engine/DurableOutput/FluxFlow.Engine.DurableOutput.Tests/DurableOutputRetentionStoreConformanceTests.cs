using Shouldly;
using Xunit;

namespace FluxFlow.Engine.DurableOutput.Tests;

/// <summary>
/// Provider-neutral behavioral contract for explicit bounded output retention.
/// Concrete provider test projects inherit this suite unchanged.
/// </summary>
public abstract class DurableOutputRetentionStoreConformanceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);

    protected abstract ValueTask<DurableOutputRetentionStoreTestContext> CreateStoreAsync();

    [Fact]
    public async Task Empty_and_no_match_purges_return_zero_without_mutation()
    {
        await using var context = await CreateStoreAsync();

        (await context.Retention.PurgeCompletedAsync(Request(Now)))
            .ShouldBe(new DurableOutputRetentionResult(0));
        (await context.Retention.PurgeDeadLettersAsync(Request(Now)))
            .ShouldBe(new DurableOutputRetentionResult(0));

        var completed = DurableOutputStoreConformanceData.Envelope("no-match-completed");
        var deadLetter = DurableOutputStoreConformanceData.Envelope("no-match-dead");
        await CompleteAsync(context, completed, Now);
        await DeadLetterAsync(context, deadLetter, Now);

        (await context.Retention.PurgeCompletedAsync(Request(Now))).DeletedCount.ShouldBe(0);
        (await context.Retention.PurgeDeadLettersAsync(Request(Now))).DeletedCount.ShouldBe(0);
        var status = await StatusAsync(context);
        status.CapturedCount.ShouldBe(2);
        status.CompletedCount.ShouldBe(1);
        status.DeadLetteredCount.ShouldBe(1);
        (await context.DeadLetters.GetAsync(deadLetter.Key)).ShouldNotBeNull();
    }

    [Fact]
    public async Task Completed_purge_uses_exclusive_utc_cutoff_and_preserves_every_other_state()
    {
        await using var context = await CreateStoreAsync();
        var old = DurableOutputStoreConformanceData.Envelope("completed-old");
        var equal = DurableOutputStoreConformanceData.Envelope("completed-equal");
        var newer = DurableOutputStoreConformanceData.Envelope("completed-newer");
        var pending = DurableOutputStoreConformanceData.Envelope("completed-pending");
        var leased = DurableOutputStoreConformanceData.Envelope("completed-leased");
        var dead = DurableOutputStoreConformanceData.Envelope("completed-dead");
        var unmaterialized = DurableOutputStoreConformanceData.Envelope("completed-unmaterialized");
        await CompleteAsync(context, old, Now.AddTicks(-1));
        await CompleteAsync(context, equal, Now);
        await CompleteAsync(context, newer, Now.AddTicks(1));
        await MakePendingAsync(context, pending, Now.AddMinutes(-2), Now.AddDays(1));
        await EnqueueAndLeaseAsync(context, leased, Now.AddMinutes(-1), TimeSpan.FromDays(2));
        await DeadLetterAsync(context, dead, Now.AddTicks(-1));
        (await context.CaptureStore.EnqueueAsync(unmaterialized)).Status
            .ShouldBe(DurableOutputEnqueueStatus.Enqueued);

        var result = await context.Retention.PurgeCompletedAsync(
            Request(Now.ToOffset(TimeSpan.FromHours(5))));

        result.DeletedCount.ShouldBe(1);
        result.DeletedCount.ShouldBeLessThanOrEqualTo(100);
        var status = await StatusAsync(context);
        status.CapturedCount.ShouldBe(6);
        status.UnmaterializedCount.ShouldBe(1);
        status.PendingCount.ShouldBe(1);
        status.LeasedCount.ShouldBe(1);
        status.CompletedCount.ShouldBe(2);
        status.DeadLetteredCount.ShouldBe(1);
        (await context.CaptureStore.EnqueueAsync(old)).Status.ShouldBe(DurableOutputEnqueueStatus.Enqueued);
        (await context.CaptureStore.EnqueueAsync(equal)).Status.ShouldBe(DurableOutputEnqueueStatus.AlreadyExists);
        (await context.CaptureStore.EnqueueAsync(newer)).Status.ShouldBe(DurableOutputEnqueueStatus.AlreadyExists);
        (await context.CaptureStore.EnqueueAsync(pending)).Status.ShouldBe(DurableOutputEnqueueStatus.AlreadyExists);
        (await context.CaptureStore.EnqueueAsync(leased)).Status.ShouldBe(DurableOutputEnqueueStatus.AlreadyExists);
        (await context.CaptureStore.EnqueueAsync(dead)).Status.ShouldBe(DurableOutputEnqueueStatus.AlreadyExists);
        (await context.CaptureStore.EnqueueAsync(unmaterialized)).Status
            .ShouldBe(DurableOutputEnqueueStatus.AlreadyExists);
    }

    [Fact]
    public async Task Dead_letter_purge_uses_exclusive_utc_cutoff_and_preserves_replayed_and_completed_rows()
    {
        await using var context = await CreateStoreAsync();
        var old = DurableOutputStoreConformanceData.Envelope("dead-old");
        var equal = DurableOutputStoreConformanceData.Envelope("dead-equal");
        var newer = DurableOutputStoreConformanceData.Envelope("dead-newer");
        var replayed = DurableOutputStoreConformanceData.Envelope("dead-replayed");
        var completed = DurableOutputStoreConformanceData.Envelope("dead-completed");
        await DeadLetterAsync(context, old, Now.AddTicks(-1));
        await DeadLetterAsync(context, equal, Now);
        await DeadLetterAsync(context, newer, Now.AddTicks(1));
        await DeadLetterAsync(context, replayed, Now.AddHours(-1));
        (await context.DeadLetters.ReplayAsync(new DurableOutputReplay(
            replayed.Key,
            expectedGeneration: 1,
            replayedAt: Now.AddMinutes(1),
            nextAttemptAt: Now.AddMinutes(2)))).Status.ShouldBe(DurableOutputReplayStatus.Replayed);
        await CompleteAsync(context, completed, Now.AddHours(-1));

        var result = await context.Retention.PurgeDeadLettersAsync(
            Request(Now.ToOffset(TimeSpan.FromHours(-4))));

        result.DeletedCount.ShouldBe(1);
        var status = await StatusAsync(context);
        status.CapturedCount.ShouldBe(4);
        status.PendingCount.ShouldBe(1);
        status.CompletedCount.ShouldBe(1);
        status.DeadLetteredCount.ShouldBe(2);
        (await context.DeadLetters.GetAsync(old.Key)).ShouldBeNull();
        (await context.DeadLetters.GetAsync(equal.Key)).ShouldNotBeNull();
        (await context.DeadLetters.GetAsync(newer.Key)).ShouldNotBeNull();
        (await context.DeadLetters.GetAsync(replayed.Key)).ShouldBeNull();
        (await context.CaptureStore.EnqueueAsync(old)).Status.ShouldBe(DurableOutputEnqueueStatus.Enqueued);
        (await context.CaptureStore.EnqueueAsync(equal)).Status.ShouldBe(DurableOutputEnqueueStatus.AlreadyExists);
        (await context.CaptureStore.EnqueueAsync(newer)).Status.ShouldBe(DurableOutputEnqueueStatus.AlreadyExists);
        (await context.CaptureStore.EnqueueAsync(replayed)).Status.ShouldBe(DurableOutputEnqueueStatus.AlreadyExists);
        (await context.CaptureStore.EnqueueAsync(completed)).Status.ShouldBe(DurableOutputEnqueueStatus.AlreadyExists);
    }

    [Fact]
    public async Task Both_purges_apply_exact_optional_address_scope()
    {
        await using var context = await CreateStoreAsync();
        var primaryCompleted = DurableOutputStoreConformanceData.Envelope("scoped-completed-primary");
        var secondaryCompleted = DurableOutputStoreConformanceData.Envelope(
            "scoped-completed-secondary",
            DurableOutputStoreConformanceData.SecondaryOutput);
        var primaryDead = DurableOutputStoreConformanceData.Envelope("scoped-dead-primary");
        var secondaryDead = DurableOutputStoreConformanceData.Envelope(
            "scoped-dead-secondary",
            DurableOutputStoreConformanceData.SecondaryOutput);
        await CompleteAsync(context, primaryCompleted, Now.AddHours(-1));
        await CompleteAsync(context, secondaryCompleted, Now.AddHours(-1));
        await DeadLetterAsync(context, primaryDead, Now.AddHours(-1));
        await DeadLetterAsync(context, secondaryDead, Now.AddHours(-1));

        (await context.Retention.PurgeCompletedAsync(Request(
            Now,
            DurableOutputStoreConformanceData.Output))).DeletedCount.ShouldBe(1);
        (await context.Retention.PurgeDeadLettersAsync(Request(
            Now,
            DurableOutputStoreConformanceData.SecondaryOutput))).DeletedCount.ShouldBe(1);

        var status = await StatusAsync(context);
        status.CapturedCount.ShouldBe(2);
        status.CompletedCount.ShouldBe(1);
        status.DeadLetteredCount.ShouldBe(1);
        (await context.CaptureStore.EnqueueAsync(primaryCompleted)).Status
            .ShouldBe(DurableOutputEnqueueStatus.Enqueued);
        (await context.CaptureStore.EnqueueAsync(secondaryCompleted)).Status
            .ShouldBe(DurableOutputEnqueueStatus.AlreadyExists);
        (await context.CaptureStore.EnqueueAsync(primaryDead)).Status
            .ShouldBe(DurableOutputEnqueueStatus.AlreadyExists);
        (await context.CaptureStore.EnqueueAsync(secondaryDead)).Status
            .ShouldBe(DurableOutputEnqueueStatus.Enqueued);
    }

    [Fact]
    public async Task Completed_purge_is_bounded_oldest_first_and_repeats_until_short_batch()
    {
        await using var context = await CreateStoreAsync();
        var candidates = new[]
        {
            Candidate("completed-newest", DurableOutputStoreConformanceData.Output, Now.AddMinutes(-1)),
            Candidate("z-message", DurableOutputStoreConformanceData.Output, Now.AddMinutes(-2)),
            Candidate("a-message", DurableOutputStoreConformanceData.SecondaryOutput, Now.AddMinutes(-2)),
            Candidate("a-message", DurableOutputStoreConformanceData.Output, Now.AddMinutes(-2)),
            Candidate("completed-oldest", DurableOutputStoreConformanceData.SecondaryOutput, Now.AddMinutes(-3))
        };
        foreach (var candidate in candidates)
            await CompleteAsync(context, candidate.Envelope, candidate.TerminalAt);
        var expectedOrder = candidates
            .OrderBy(static candidate => candidate.TerminalAt.UtcTicks)
            .ThenBy(static candidate => candidate.Envelope.Address.Value, StringComparer.Ordinal)
            .ThenBy(static candidate => candidate.Envelope.MessageId.Value, StringComparer.Ordinal)
            .Select(static candidate => candidate.Envelope.Key)
            .ToArray();
        var request = Request(Now, maxCount: 2);

        (await context.Retention.PurgeCompletedAsync(request)).DeletedCount.ShouldBe(2);
        var observedDeleted = new List<DurableOutputKey>();
        foreach (var candidate in candidates)
        {
            var enqueue = await context.CaptureStore.EnqueueAsync(candidate.Envelope);
            if (enqueue.Status == DurableOutputEnqueueStatus.Enqueued)
                observedDeleted.Add(candidate.Envelope.Key);
            else
                enqueue.Status.ShouldBe(DurableOutputEnqueueStatus.AlreadyExists);
        }
        observedDeleted.ShouldBe(expectedOrder.Take(2), ignoreOrder: true);

        var counts = new[]
        {
            (await context.Retention.PurgeCompletedAsync(request)).DeletedCount,
            (await context.Retention.PurgeCompletedAsync(request)).DeletedCount,
            (await context.Retention.PurgeCompletedAsync(request)).DeletedCount
        };
        counts.ShouldBe([2, 1, 0]);
        counts.ShouldAllBe(count => count <= request.MaxCount);
        var status = await StatusAsync(context);
        status.CompletedCount.ShouldBe(0);
        status.CapturedCount.ShouldBe(2);
        status.UnmaterializedCount.ShouldBe(2);
    }

    [Fact]
    public async Task Dead_letter_purge_is_bounded_oldest_first_and_repeats_until_short_batch()
    {
        await using var context = await CreateStoreAsync();
        var candidates = new[]
        {
            Candidate("dead-newest", DurableOutputStoreConformanceData.Output, Now.AddMinutes(-1)),
            Candidate("z-message", DurableOutputStoreConformanceData.Output, Now.AddMinutes(-2)),
            Candidate("a-message", DurableOutputStoreConformanceData.SecondaryOutput, Now.AddMinutes(-2)),
            Candidate("a-message", DurableOutputStoreConformanceData.Output, Now.AddMinutes(-2)),
            Candidate("dead-oldest", DurableOutputStoreConformanceData.SecondaryOutput, Now.AddMinutes(-3))
        };
        foreach (var candidate in candidates)
            await DeadLetterAsync(context, candidate.Envelope, candidate.TerminalAt);
        var expectedOrder = candidates
            .OrderBy(static candidate => candidate.TerminalAt.UtcTicks)
            .ThenBy(static candidate => candidate.Envelope.Address.Value, StringComparer.Ordinal)
            .ThenBy(static candidate => candidate.Envelope.MessageId.Value, StringComparer.Ordinal)
            .Select(static candidate => candidate.Envelope.Key)
            .ToArray();
        var request = Request(Now, maxCount: 2);

        (await context.Retention.PurgeDeadLettersAsync(request)).DeletedCount.ShouldBe(2);
        var observedDeleted = new List<DurableOutputKey>();
        foreach (var candidate in candidates)
        {
            if (await context.DeadLetters.GetAsync(candidate.Envelope.Key) is null)
                observedDeleted.Add(candidate.Envelope.Key);
        }
        observedDeleted.ShouldBe(expectedOrder.Take(2), ignoreOrder: true);

        var counts = new[]
        {
            (await context.Retention.PurgeDeadLettersAsync(request)).DeletedCount,
            (await context.Retention.PurgeDeadLettersAsync(request)).DeletedCount,
            (await context.Retention.PurgeDeadLettersAsync(request)).DeletedCount
        };
        counts.ShouldBe([2, 1, 0]);
        counts.ShouldAllBe(count => count <= request.MaxCount);
        (await StatusAsync(context)).CapturedCount.ShouldBe(0);
    }

    [Fact]
    public async Task Purging_each_terminal_state_removes_capture_and_delivery_then_allows_recapture()
    {
        await using var context = await CreateStoreAsync();
        var completed = DurableOutputStoreConformanceData.Envelope("reuse-completed");
        var deadLetter = DurableOutputStoreConformanceData.Envelope("reuse-dead-letter");
        await CompleteAsync(context, completed, Now.AddHours(-1));
        await DeadLetterAsync(context, deadLetter, Now.AddHours(-1));

        (await context.Retention.PurgeCompletedAsync(Request(Now))).DeletedCount.ShouldBe(1);
        (await context.Retention.PurgeDeadLettersAsync(Request(Now))).DeletedCount.ShouldBe(1);

        (await context.DeadLetters.GetAsync(deadLetter.Key)).ShouldBeNull();
        (await context.CaptureStore.EnqueueAsync(completed)).Status
            .ShouldBe(DurableOutputEnqueueStatus.Enqueued);
        (await context.CaptureStore.EnqueueAsync(deadLetter)).Status
            .ShouldBe(DurableOutputEnqueueStatus.Enqueued);
        var status = await StatusAsync(context);
        status.CapturedCount.ShouldBe(2);
        status.UnmaterializedCount.ShouldBe(2);
        status.TrackedDeliveryCount.ShouldBe(0);
    }

    [Fact]
    public async Task Unmaterialized_capture_only_rows_are_never_purged()
    {
        await using var context = await CreateStoreAsync();
        var unmaterialized = DurableOutputStoreConformanceData.Envelope("capture-only");
        (await context.CaptureStore.EnqueueAsync(unmaterialized)).Status
            .ShouldBe(DurableOutputEnqueueStatus.Enqueued);

        (await context.Retention.PurgeCompletedAsync(Request(Now.AddDays(1)))).DeletedCount
            .ShouldBe(0);
        (await context.Retention.PurgeDeadLettersAsync(Request(Now.AddDays(1)))).DeletedCount
            .ShouldBe(0);

        (await context.CaptureStore.EnqueueAsync(unmaterialized)).Status
            .ShouldBe(DurableOutputEnqueueStatus.AlreadyExists);
        var status = await StatusAsync(context);
        status.CapturedCount.ShouldBe(1);
        status.UnmaterializedCount.ShouldBe(1);
        status.TrackedDeliveryCount.ShouldBe(0);
    }

    [Fact]
    public async Task Concurrent_completed_purges_cannot_double_count_one_capture()
    {
        await using var context = await CreateStoreAsync();
        var completed = DurableOutputStoreConformanceData.Envelope("concurrent-completed");
        await CompleteAsync(context, completed, Now.AddHours(-1));
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var operations = Enumerable.Range(0, 2).Select(_ => Task.Run(async () =>
        {
            await start.Task;
            return await context.Retention.PurgeCompletedAsync(Request(Now, maxCount: 1));
        })).ToArray();

        start.TrySetResult();
        var results = await Task.WhenAll(operations);

        results.Select(static result => result.DeletedCount).Order().ShouldBe([0, 1]);
        results.Sum(static result => result.DeletedCount).ShouldBe(1);
        (await StatusAsync(context)).CapturedCount.ShouldBe(0);
        (await context.CaptureStore.EnqueueAsync(completed)).Status
            .ShouldBe(DurableOutputEnqueueStatus.Enqueued);
    }

    [Fact]
    public async Task Replay_and_dead_letter_purge_have_exactly_one_valid_winner()
    {
        await using var context = await CreateStoreAsync();
        var deadLetter = DurableOutputStoreConformanceData.Envelope("replay-purge-race");
        await DeadLetterAsync(context, deadLetter, Now.AddHours(-1));
        var replay = new DurableOutputReplay(deadLetter.Key, 1, Now, Now);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var purgeTask = Task.Run(async () =>
        {
            await start.Task;
            return await context.Retention.PurgeDeadLettersAsync(Request(Now));
        });
        var replayTask = Task.Run(async () =>
        {
            await start.Task;
            return await context.DeadLetters.ReplayAsync(replay);
        });

        start.TrySetResult();
        var purge = await purgeTask;
        var replayResult = await replayTask;

        if (purge.DeletedCount == 1)
        {
            replayResult.Status.ShouldBe(DurableOutputReplayStatus.NotFound);
            (await StatusAsync(context)).CapturedCount.ShouldBe(0);
        }
        else
        {
            purge.DeletedCount.ShouldBe(0);
            replayResult.Status.ShouldBe(DurableOutputReplayStatus.Replayed);
            var status = await StatusAsync(context);
            status.CapturedCount.ShouldBe(1);
            status.PendingCount.ShouldBe(1);
            status.DeadLetteredCount.ShouldBe(0);
        }
        (await context.DeadLetters.GetAsync(deadLetter.Key)).ShouldBeNull();
    }

    [Fact]
    public async Task Completion_transition_and_purge_leave_one_valid_atomic_outcome()
    {
        await using var context = await CreateStoreAsync();
        var envelope = DurableOutputStoreConformanceData.Envelope("transition-purge-race");
        var lease = await EnqueueAndLeaseAsync(context, envelope, Now.AddMinutes(-2));
        var transition = new DurableOutputDeliveryTransition(
            envelope.Key,
            lease.LeaseToken,
            Now.AddMinutes(-1));
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var purgeTask = Task.Run(async () =>
        {
            await start.Task;
            return await context.Retention.PurgeCompletedAsync(Request(Now));
        });
        var transitionTask = Task.Run(async () =>
        {
            await start.Task;
            return await context.DeliveryStore.CompleteAsync(transition);
        });

        start.TrySetResult();
        var purge = await purgeTask;
        var transitionResult = await transitionTask;

        transitionResult.Status.ShouldBe(DurableOutputDeliveryTransitionStatus.Applied);
        if (purge.DeletedCount == 1)
        {
            (await StatusAsync(context)).CapturedCount.ShouldBe(0);
        }
        else
        {
            purge.DeletedCount.ShouldBe(0);
            (await StatusAsync(context)).CompletedCount.ShouldBe(1);
            (await context.Retention.PurgeCompletedAsync(Request(Now))).DeletedCount.ShouldBe(1);
        }
    }

    [Fact]
    public async Task Precancelled_purges_throw_and_leave_parent_and_child_rows_intact()
    {
        await using var context = await CreateStoreAsync();
        var completed = DurableOutputStoreConformanceData.Envelope("cancel-completed");
        var deadLetter = DurableOutputStoreConformanceData.Envelope("cancel-dead-letter");
        await CompleteAsync(context, completed, Now.AddHours(-1));
        await DeadLetterAsync(context, deadLetter, Now.AddHours(-1));
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(() =>
            context.Retention.PurgeCompletedAsync(Request(Now), canceled.Token).AsTask());
        await Should.ThrowAsync<OperationCanceledException>(() =>
            context.Retention.PurgeDeadLettersAsync(Request(Now), canceled.Token).AsTask());

        var status = await StatusAsync(context);
        status.CapturedCount.ShouldBe(2);
        status.CompletedCount.ShouldBe(1);
        status.DeadLetteredCount.ShouldBe(1);
        (await context.CaptureStore.EnqueueAsync(completed)).Status
            .ShouldBe(DurableOutputEnqueueStatus.AlreadyExists);
        (await context.CaptureStore.EnqueueAsync(deadLetter)).Status
            .ShouldBe(DurableOutputEnqueueStatus.AlreadyExists);
        (await context.DeadLetters.GetAsync(deadLetter.Key)).ShouldNotBeNull();
    }

    private static DurableOutputRetentionRequest Request(
        DateTimeOffset terminalBefore,
        FluxFlow.Composition.Addressing.ApplicationAddress? address = null,
        int maxCount = DurableOutputRetentionRequest.DefaultMaxCount)
        => new(terminalBefore, address, maxCount);

    private static RetentionCandidate Candidate(
        string messageId,
        FluxFlow.Composition.Addressing.ApplicationAddress address,
        DateTimeOffset terminalAt)
        => new(DurableOutputStoreConformanceData.Envelope(messageId, address), terminalAt);

    private static async ValueTask<DurableOutputDeliveryLease> EnqueueAndLeaseAsync(
        DurableOutputRetentionStoreTestContext context,
        DurableOutputEnvelope envelope,
        DateTimeOffset leaseAt,
        TimeSpan? leaseDuration = null)
    {
        (await context.CaptureStore.EnqueueAsync(envelope)).Status
            .ShouldBe(DurableOutputEnqueueStatus.Enqueued);
        var lease = (await context.DeliveryStore.TryLeaseAsync(
            DurableOutputStoreConformanceData.DeliveryRequest(
                leaseAt,
                ownerId: $"retention-{envelope.MessageId.Value}",
                leaseDuration: leaseDuration ?? TimeSpan.FromHours(1))))
            .ShouldNotBeNull();
        lease.Envelope.Key.ShouldBe(envelope.Key);
        return lease;
    }

    private static async ValueTask CompleteAsync(
        DurableOutputRetentionStoreTestContext context,
        DurableOutputEnvelope envelope,
        DateTimeOffset completedAt)
    {
        var lease = await EnqueueAndLeaseAsync(context, envelope, completedAt.AddMinutes(-1));
        var result = await context.DeliveryStore.CompleteAsync(new DurableOutputDeliveryTransition(
            envelope.Key,
            lease.LeaseToken,
            completedAt));
        result.Status.ShouldBe(DurableOutputDeliveryTransitionStatus.Applied);
    }

    private static async ValueTask DeadLetterAsync(
        DurableOutputRetentionStoreTestContext context,
        DurableOutputEnvelope envelope,
        DateTimeOffset deadLetteredAt)
    {
        var lease = await EnqueueAndLeaseAsync(context, envelope, deadLetteredAt.AddMinutes(-1));
        var result = await context.DeliveryStore.DeadLetterAsync(
            DurableOutputStoreConformanceData.DeadLetter(
                envelope.Key,
                lease.LeaseToken,
                deadLetteredAt));
        result.Status.ShouldBe(DurableOutputDeliveryTransitionStatus.Applied);
    }

    private static async ValueTask MakePendingAsync(
        DurableOutputRetentionStoreTestContext context,
        DurableOutputEnvelope envelope,
        DateTimeOffset releasedAt,
        DateTimeOffset nextAttemptAt)
    {
        var lease = await EnqueueAndLeaseAsync(context, envelope, releasedAt.AddMinutes(-1));
        var result = await context.DeliveryStore.RetryAsync(new DurableOutputDeliveryRetry(
            envelope.Key,
            lease.LeaseToken,
            releasedAt,
            nextAttemptAt));
        result.Status.ShouldBe(DurableOutputDeliveryTransitionStatus.Applied);
    }

    private static ValueTask<DurableOutputStatusSnapshot> StatusAsync(
        DurableOutputRetentionStoreTestContext context)
        => context.Status.GetStatusAsync(new DurableOutputStatusQuery(Now.AddDays(3)));

    private sealed record RetentionCandidate(
        DurableOutputEnvelope Envelope,
        DateTimeOffset TerminalAt);
}
