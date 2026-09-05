using Shouldly;
using Xunit;

namespace FluxFlow.Engine.DurableInput.Tests;

/// <summary>
/// Provider-neutral behavioral contract for explicit bounded input retention.
/// Concrete provider test projects inherit this suite unchanged.
/// </summary>
public abstract class DurableInputRetentionStoreConformanceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);

    protected abstract ValueTask<DurableInputRetentionStoreTestContext> CreateStoreAsync();

    [Fact]
    public async Task Empty_and_no_match_purges_return_zero_without_mutation()
    {
        await using var context = await CreateStoreAsync();
        var cutoff = Now;

        (await context.Retention.PurgeDeliveredAsync(Request(cutoff)))
            .ShouldBe(new DurableInputRetentionResult(0));
        (await context.Retention.PurgeDeadLettersAsync(Request(cutoff)))
            .ShouldBe(new DurableInputRetentionResult(0));

        var delivered = DurableInputStoreConformanceData.Envelope("no-match-delivered");
        var deadLetter = DurableInputStoreConformanceData.Envelope("no-match-dead");
        await DeliverAsync(context, delivered, cutoff);
        await DeadLetterAsync(context, deadLetter, cutoff);

        (await context.Retention.PurgeDeliveredAsync(Request(cutoff))).DeletedCount.ShouldBe(0);
        (await context.Retention.PurgeDeadLettersAsync(Request(cutoff))).DeletedCount.ShouldBe(0);
        var status = await StatusAsync(context);
        status.TotalCount.ShouldBe(2);
        status.DeliveredCount.ShouldBe(1);
        status.DeadLetteredCount.ShouldBe(1);
        (await context.DeadLetters.GetAsync(deadLetter.Key)).ShouldNotBeNull();
    }

    [Fact]
    public async Task Delivered_purge_uses_exclusive_utc_cutoff_and_preserves_every_other_state()
    {
        await using var context = await CreateStoreAsync();
        var old = DurableInputStoreConformanceData.Envelope("delivered-old");
        var equal = DurableInputStoreConformanceData.Envelope("delivered-equal");
        var newer = DurableInputStoreConformanceData.Envelope("delivered-newer");
        var pending = DurableInputStoreConformanceData.Envelope("delivered-pending");
        var leased = DurableInputStoreConformanceData.Envelope("delivered-leased");
        var dead = DurableInputStoreConformanceData.Envelope("delivered-dead");
        await DeliverAsync(context, old, Now.AddTicks(-1));
        await DeliverAsync(context, equal, Now);
        await DeliverAsync(context, newer, Now.AddTicks(1));
        (await context.Store.EnqueueAsync(pending)).Status.ShouldBe(DurableInputEnqueueStatus.Enqueued);
        await EnqueueAndLeaseAsync(context, leased, Now.AddMinutes(-1), TimeSpan.FromDays(2));
        await DeadLetterAsync(context, dead, Now.AddTicks(-1));

        var equivalentOffsetCutoff = Now.ToOffset(TimeSpan.FromHours(5));
        var result = await context.Retention.PurgeDeliveredAsync(Request(equivalentOffsetCutoff));

        result.DeletedCount.ShouldBe(1);
        result.DeletedCount.ShouldBeLessThanOrEqualTo(100);
        var status = await StatusAsync(context);
        status.PendingCount.ShouldBe(1);
        status.LeasedCount.ShouldBe(1);
        status.DeliveredCount.ShouldBe(2);
        status.DeadLetteredCount.ShouldBe(1);
        status.TotalCount.ShouldBe(5);
        (await context.Store.EnqueueAsync(old)).Status.ShouldBe(DurableInputEnqueueStatus.Enqueued);
        (await context.Store.EnqueueAsync(equal)).Status.ShouldBe(DurableInputEnqueueStatus.AlreadyExists);
        (await context.Store.EnqueueAsync(newer)).Status.ShouldBe(DurableInputEnqueueStatus.AlreadyExists);
        (await context.Store.EnqueueAsync(pending)).Status.ShouldBe(DurableInputEnqueueStatus.AlreadyExists);
        (await context.Store.EnqueueAsync(leased)).Status.ShouldBe(DurableInputEnqueueStatus.AlreadyExists);
        (await context.Store.EnqueueAsync(dead)).Status.ShouldBe(DurableInputEnqueueStatus.AlreadyExists);
    }

    [Fact]
    public async Task Dead_letter_purge_uses_exclusive_utc_cutoff_and_preserves_replayed_and_delivered_rows()
    {
        await using var context = await CreateStoreAsync();
        var old = DurableInputStoreConformanceData.Envelope("dead-old");
        var equal = DurableInputStoreConformanceData.Envelope("dead-equal");
        var newer = DurableInputStoreConformanceData.Envelope("dead-newer");
        var replayed = DurableInputStoreConformanceData.Envelope("dead-replayed");
        var delivered = DurableInputStoreConformanceData.Envelope("dead-delivered");
        await DeadLetterAsync(context, old, Now.AddTicks(-1));
        await DeadLetterAsync(context, equal, Now);
        await DeadLetterAsync(context, newer, Now.AddTicks(1));
        await DeadLetterAsync(context, replayed, Now.AddHours(-1));
        (await context.DeadLetters.ReplayAsync(new DurableInputReplay(
            replayed.Key,
            expectedGeneration: 1,
            replayedAt: Now.AddMinutes(1),
            nextAttemptAt: Now.AddMinutes(2)))).Status.ShouldBe(DurableInputReplayStatus.Replayed);
        await DeliverAsync(context, delivered, Now.AddHours(-1));

        var result = await context.Retention.PurgeDeadLettersAsync(
            Request(Now.ToOffset(TimeSpan.FromHours(-4))));

        result.DeletedCount.ShouldBe(1);
        var status = await StatusAsync(context);
        status.PendingCount.ShouldBe(1);
        status.DeliveredCount.ShouldBe(1);
        status.DeadLetteredCount.ShouldBe(2);
        status.TotalCount.ShouldBe(4);
        (await context.DeadLetters.GetAsync(old.Key)).ShouldBeNull();
        (await context.DeadLetters.GetAsync(equal.Key)).ShouldNotBeNull();
        (await context.DeadLetters.GetAsync(newer.Key)).ShouldNotBeNull();
        (await context.DeadLetters.GetAsync(replayed.Key)).ShouldBeNull();
        (await context.Store.EnqueueAsync(old)).Status.ShouldBe(DurableInputEnqueueStatus.Enqueued);
        (await context.Store.EnqueueAsync(equal)).Status.ShouldBe(DurableInputEnqueueStatus.AlreadyExists);
        (await context.Store.EnqueueAsync(newer)).Status.ShouldBe(DurableInputEnqueueStatus.AlreadyExists);
        (await context.Store.EnqueueAsync(replayed)).Status.ShouldBe(DurableInputEnqueueStatus.AlreadyExists);
        (await context.Store.EnqueueAsync(delivered)).Status.ShouldBe(DurableInputEnqueueStatus.AlreadyExists);
    }

    [Fact]
    public async Task Both_purges_apply_exact_optional_address_scope()
    {
        await using var context = await CreateStoreAsync();
        var primaryDelivered = DurableInputStoreConformanceData.Envelope("scoped-delivered-primary");
        var secondaryDelivered = DurableInputStoreConformanceData.Envelope(
            "scoped-delivered-secondary",
            address: DurableInputStoreConformanceData.SecondaryInput);
        var primaryDead = DurableInputStoreConformanceData.Envelope("scoped-dead-primary");
        var secondaryDead = DurableInputStoreConformanceData.Envelope(
            "scoped-dead-secondary",
            address: DurableInputStoreConformanceData.SecondaryInput);
        await DeliverAsync(context, primaryDelivered, Now.AddHours(-1));
        await DeliverAsync(context, secondaryDelivered, Now.AddHours(-1));
        await DeadLetterAsync(context, primaryDead, Now.AddHours(-1));
        await DeadLetterAsync(context, secondaryDead, Now.AddHours(-1));

        (await context.Retention.PurgeDeliveredAsync(Request(
            Now,
            DurableInputStoreConformanceData.Input))).DeletedCount.ShouldBe(1);
        (await context.Retention.PurgeDeadLettersAsync(Request(
            Now,
            DurableInputStoreConformanceData.SecondaryInput))).DeletedCount.ShouldBe(1);

        var status = await StatusAsync(context);
        status.DeliveredCount.ShouldBe(1);
        status.DeadLetteredCount.ShouldBe(1);
        status.TotalCount.ShouldBe(2);
        (await context.Store.EnqueueAsync(primaryDelivered)).Status
            .ShouldBe(DurableInputEnqueueStatus.Enqueued);
        (await context.Store.EnqueueAsync(secondaryDelivered)).Status
            .ShouldBe(DurableInputEnqueueStatus.AlreadyExists);
        (await context.Store.EnqueueAsync(primaryDead)).Status
            .ShouldBe(DurableInputEnqueueStatus.AlreadyExists);
        (await context.Store.EnqueueAsync(secondaryDead)).Status
            .ShouldBe(DurableInputEnqueueStatus.Enqueued);
    }

    [Fact]
    public async Task Delivered_purge_is_bounded_oldest_first_and_repeats_until_short_batch()
    {
        await using var context = await CreateStoreAsync();
        var candidates = new[]
        {
            Candidate("delivered-newest", DurableInputStoreConformanceData.Input, Now.AddMinutes(-1)),
            Candidate("z-message", DurableInputStoreConformanceData.Input, Now.AddMinutes(-2)),
            Candidate("a-message", DurableInputStoreConformanceData.SecondaryInput, Now.AddMinutes(-2)),
            Candidate("a-message", DurableInputStoreConformanceData.Input, Now.AddMinutes(-2)),
            Candidate("delivered-oldest", DurableInputStoreConformanceData.SecondaryInput, Now.AddMinutes(-3))
        };
        foreach (var candidate in candidates)
            await DeliverAsync(context, candidate.Envelope, candidate.TerminalAt);
        var expectedOrder = candidates
            .OrderBy(static candidate => candidate.TerminalAt.UtcTicks)
            .ThenBy(static candidate => candidate.Envelope.Address.Value, StringComparer.Ordinal)
            .ThenBy(static candidate => candidate.Envelope.MessageId.Value, StringComparer.Ordinal)
            .Select(static candidate => candidate.Envelope.Key)
            .ToArray();
        var request = Request(Now, maxCount: 2);

        (await context.Retention.PurgeDeliveredAsync(request)).DeletedCount.ShouldBe(2);
        var observedDeleted = new List<DurableInputKey>();
        foreach (var candidate in candidates)
        {
            var enqueue = await context.Store.EnqueueAsync(candidate.Envelope);
            if (enqueue.Status == DurableInputEnqueueStatus.Enqueued)
                observedDeleted.Add(candidate.Envelope.Key);
            else
                enqueue.Status.ShouldBe(DurableInputEnqueueStatus.AlreadyExists);
        }
        observedDeleted.ShouldBe(expectedOrder.Take(2), ignoreOrder: true);

        var counts = new[]
        {
            (await context.Retention.PurgeDeliveredAsync(request)).DeletedCount,
            (await context.Retention.PurgeDeliveredAsync(request)).DeletedCount,
            (await context.Retention.PurgeDeliveredAsync(request)).DeletedCount
        };
        counts.ShouldBe([2, 1, 0]);
        counts.ShouldAllBe(count => count <= request.MaxCount);
        (await StatusAsync(context)).DeliveredCount.ShouldBe(0);
    }

    [Fact]
    public async Task Dead_letter_purge_is_bounded_oldest_first_and_repeats_until_short_batch()
    {
        await using var context = await CreateStoreAsync();
        var candidates = new[]
        {
            Candidate("dead-newest", DurableInputStoreConformanceData.Input, Now.AddMinutes(-1)),
            Candidate("z-message", DurableInputStoreConformanceData.Input, Now.AddMinutes(-2)),
            Candidate("a-message", DurableInputStoreConformanceData.SecondaryInput, Now.AddMinutes(-2)),
            Candidate("a-message", DurableInputStoreConformanceData.Input, Now.AddMinutes(-2)),
            Candidate("dead-oldest", DurableInputStoreConformanceData.SecondaryInput, Now.AddMinutes(-3))
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
        var observedDeleted = new List<DurableInputKey>();
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
        (await StatusAsync(context)).DeadLetteredCount.ShouldBe(0);
    }

    [Fact]
    public async Task Purged_delivered_and_dead_letter_identities_can_be_enqueued_as_new()
    {
        await using var context = await CreateStoreAsync();
        var delivered = DurableInputStoreConformanceData.Envelope("reuse-delivered");
        var deadLetter = DurableInputStoreConformanceData.Envelope("reuse-dead-letter");
        await DeliverAsync(context, delivered, Now.AddHours(-1));
        await DeadLetterAsync(context, deadLetter, Now.AddHours(-1));

        (await context.Retention.PurgeDeliveredAsync(Request(Now))).DeletedCount.ShouldBe(1);
        (await context.Retention.PurgeDeadLettersAsync(Request(Now))).DeletedCount.ShouldBe(1);

        (await context.Store.EnqueueAsync(delivered)).Status.ShouldBe(DurableInputEnqueueStatus.Enqueued);
        (await context.Store.EnqueueAsync(deadLetter)).Status.ShouldBe(DurableInputEnqueueStatus.Enqueued);
        var status = await StatusAsync(context);
        status.PendingCount.ShouldBe(2);
        status.DeliveredCount.ShouldBe(0);
        status.DeadLetteredCount.ShouldBe(0);
        status.TotalCount.ShouldBe(2);
    }

    [Fact]
    public async Task Concurrent_delivered_purges_cannot_double_count_one_record()
    {
        await using var context = await CreateStoreAsync();
        var delivered = DurableInputStoreConformanceData.Envelope("concurrent-delivered");
        await DeliverAsync(context, delivered, Now.AddHours(-1));
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var operations = Enumerable.Range(0, 2).Select(_ => Task.Run(async () =>
        {
            await start.Task;
            return await context.Retention.PurgeDeliveredAsync(Request(Now, maxCount: 1));
        })).ToArray();

        start.TrySetResult();
        var results = await Task.WhenAll(operations);

        results.Select(static result => result.DeletedCount).Order().ShouldBe([0, 1]);
        results.Sum(static result => result.DeletedCount).ShouldBe(1);
        (await StatusAsync(context)).TotalCount.ShouldBe(0);
        (await context.Store.EnqueueAsync(delivered)).Status.ShouldBe(DurableInputEnqueueStatus.Enqueued);
    }

    [Fact]
    public async Task Replay_and_dead_letter_purge_have_exactly_one_valid_winner()
    {
        await using var context = await CreateStoreAsync();
        var deadLetter = DurableInputStoreConformanceData.Envelope("replay-purge-race");
        await DeadLetterAsync(context, deadLetter, Now.AddHours(-1));
        var replay = new DurableInputReplay(deadLetter.Key, 1, Now, Now);
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
            replayResult.Status.ShouldBe(DurableInputReplayStatus.NotFound);
            (await StatusAsync(context)).TotalCount.ShouldBe(0);
        }
        else
        {
            purge.DeletedCount.ShouldBe(0);
            replayResult.Status.ShouldBe(DurableInputReplayStatus.Replayed);
            var status = await StatusAsync(context);
            status.PendingCount.ShouldBe(1);
            status.DeadLetteredCount.ShouldBe(0);
            status.TotalCount.ShouldBe(1);
        }
        (await context.DeadLetters.GetAsync(deadLetter.Key)).ShouldBeNull();
    }

    [Fact]
    public async Task Delivery_transition_and_purge_leave_one_valid_atomic_outcome()
    {
        await using var context = await CreateStoreAsync();
        var envelope = DurableInputStoreConformanceData.Envelope("transition-purge-race");
        var lease = await EnqueueAndLeaseAsync(context, envelope, Now.AddMinutes(-2));
        var transition = new DurableInputLeaseTransition(
            envelope.Key,
            lease.LeaseToken,
            Now.AddMinutes(-1));
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var purgeTask = Task.Run(async () =>
        {
            await start.Task;
            return await context.Retention.PurgeDeliveredAsync(Request(Now));
        });
        var transitionTask = Task.Run(async () =>
        {
            await start.Task;
            return await context.Store.MarkDeliveredAsync(transition);
        });

        start.TrySetResult();
        var purge = await purgeTask;
        var transitionResult = await transitionTask;

        transitionResult.Status.ShouldBe(DurableInputTransitionStatus.Applied);
        if (purge.DeletedCount == 1)
        {
            (await StatusAsync(context)).TotalCount.ShouldBe(0);
        }
        else
        {
            purge.DeletedCount.ShouldBe(0);
            (await StatusAsync(context)).DeliveredCount.ShouldBe(1);
            (await context.Retention.PurgeDeliveredAsync(Request(Now))).DeletedCount.ShouldBe(1);
        }
    }

    [Fact]
    public async Task Precancelled_purges_throw_and_leave_all_terminal_rows_intact()
    {
        await using var context = await CreateStoreAsync();
        var delivered = DurableInputStoreConformanceData.Envelope("cancel-delivered");
        var deadLetter = DurableInputStoreConformanceData.Envelope("cancel-dead-letter");
        await DeliverAsync(context, delivered, Now.AddHours(-1));
        await DeadLetterAsync(context, deadLetter, Now.AddHours(-1));
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(() =>
            context.Retention.PurgeDeliveredAsync(Request(Now), canceled.Token).AsTask());
        await Should.ThrowAsync<OperationCanceledException>(() =>
            context.Retention.PurgeDeadLettersAsync(Request(Now), canceled.Token).AsTask());

        var status = await StatusAsync(context);
        status.DeliveredCount.ShouldBe(1);
        status.DeadLetteredCount.ShouldBe(1);
        status.TotalCount.ShouldBe(2);
        (await context.Store.EnqueueAsync(delivered)).Status
            .ShouldBe(DurableInputEnqueueStatus.AlreadyExists);
        (await context.Store.EnqueueAsync(deadLetter)).Status
            .ShouldBe(DurableInputEnqueueStatus.AlreadyExists);
        (await context.DeadLetters.GetAsync(deadLetter.Key)).ShouldNotBeNull();
    }

    private static DurableInputRetentionRequest Request(
        DateTimeOffset terminalBefore,
        FluxFlow.Composition.Addressing.ApplicationAddress? address = null,
        int maxCount = DurableInputRetentionRequest.DefaultMaxCount)
        => new(terminalBefore, address, maxCount);

    private static RetentionCandidate Candidate(
        string messageId,
        FluxFlow.Composition.Addressing.ApplicationAddress address,
        DateTimeOffset terminalAt)
        => new(DurableInputStoreConformanceData.Envelope(messageId, address: address), terminalAt);

    private static async ValueTask<DurableInputLease> EnqueueAndLeaseAsync(
        DurableInputRetentionStoreTestContext context,
        DurableInputEnvelope envelope,
        DateTimeOffset leaseAt,
        TimeSpan? leaseDuration = null)
    {
        (await context.Store.EnqueueAsync(envelope)).Status.ShouldBe(DurableInputEnqueueStatus.Enqueued);
        var leases = await context.Store.LeaseAsync(DurableInputStoreConformanceData.Request(
            ownerId: $"retention-{envelope.MessageId.Value}",
            now: leaseAt,
            leaseUntil: leaseAt.Add(leaseDuration ?? TimeSpan.FromHours(1)),
            maxCount: 1));
        var lease = leases.ShouldHaveSingleItem();
        lease.Envelope.Key.ShouldBe(envelope.Key);
        return lease;
    }

    private static async ValueTask DeliverAsync(
        DurableInputRetentionStoreTestContext context,
        DurableInputEnvelope envelope,
        DateTimeOffset deliveredAt)
    {
        var lease = await EnqueueAndLeaseAsync(context, envelope, deliveredAt.AddMinutes(-1));
        var result = await context.Store.MarkDeliveredAsync(new DurableInputLeaseTransition(
            envelope.Key,
            lease.LeaseToken,
            deliveredAt));
        result.Status.ShouldBe(DurableInputTransitionStatus.Applied);
    }

    private static async ValueTask DeadLetterAsync(
        DurableInputRetentionStoreTestContext context,
        DurableInputEnvelope envelope,
        DateTimeOffset deadLetteredAt)
    {
        var lease = await EnqueueAndLeaseAsync(context, envelope, deadLetteredAt.AddMinutes(-1));
        var result = await context.Store.DeadLetterAsync(new DurableInputDeadLetter(
            envelope.Key,
            lease.LeaseToken,
            deadLetteredAt,
            DurableInputStoreConformanceData.Failure()));
        result.Status.ShouldBe(DurableInputTransitionStatus.Applied);
    }

    private static ValueTask<DurableInputStatusSnapshot> StatusAsync(
        DurableInputRetentionStoreTestContext context)
        => context.Status.GetStatusAsync(new DurableInputStatusQuery(Now.AddDays(3)));

    private sealed record RetentionCandidate(
        DurableInputEnvelope Envelope,
        DateTimeOffset TerminalAt);
}
