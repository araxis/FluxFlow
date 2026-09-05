using Shouldly;
using Xunit;

namespace FluxFlow.Engine.DurableOutput.Tests;

/// <summary>
/// Executable provider-neutral conformance specification for durable-output
/// dead-letter inspection and replay stores.
/// </summary>
public abstract class DurableOutputDeadLetterStoreConformanceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 1, 12, 0, 0, TimeSpan.FromHours(2));

    protected abstract ValueTask<DurableOutputDeadLetterStoreTestContext> CreateStoreAsync();

    [Fact]
    public async Task Current_final_lease_dead_letters_generation_one_and_is_not_eligible()
    {
        await using var context = await CreateStoreAsync();
        var envelope = DurableOutputStoreConformanceData.ErrorEnvelope("dead-letter-current");

        var result = await CreateDeadLetterAsync(context, envelope, Now, Now.AddSeconds(1));

        result.ShouldBe(new DurableOutputDeliveryTransitionResult(
            envelope.Key,
            DurableOutputDeliveryTransitionStatus.Applied));
        (await context.DeliveryStore.TryLeaseAsync(Request(
            Now.AddDays(1),
            "after-dead-letter"))).ShouldBeNull();
        var details = (await context.DeadLetterStore.GetAsync(envelope.Key)).ShouldNotBeNull();
        details.Envelope.HasSameContent(envelope).ShouldBeTrue();
        details.Attempt.ShouldBe(1);
        details.Reason.ShouldBe(DurableOutputDeadLetterReason.HandlerFailure);
        ShouldHaveExactTime(details.DeadLetteredAt, Now.AddSeconds(1));
        details.Generation.ShouldBe(1);
    }

    [Fact]
    public async Task Dead_letter_transition_reports_lease_lost_for_wrong_stale_and_expired_tokens()
    {
        await using var context = await CreateStoreAsync();
        var envelope = DurableOutputStoreConformanceData.Envelope("dead-letter-lease-loss");
        await context.CaptureStore.EnqueueAsync(envelope);
        var first = (await context.DeliveryStore.TryLeaseAsync(Request(Now)))
            .ShouldNotBeNull();
        var missing = DurableOutputStoreConformanceData.Envelope("dead-letter-missing").Key;

        (await context.DeliveryStore.DeadLetterAsync(DeadLetter(
            missing,
            Guid.NewGuid(),
            Now.AddSeconds(1)))).ShouldBe(new DurableOutputDeliveryTransitionResult(
                missing,
                DurableOutputDeliveryTransitionStatus.NotFound));
        (await context.DeliveryStore.DeadLetterAsync(DeadLetter(
            envelope.Key,
            Guid.NewGuid(),
            Now.AddSeconds(1)))).Status.ShouldBe(DurableOutputDeliveryTransitionStatus.LeaseLost);
        (await context.DeliveryStore.DeadLetterAsync(DeadLetter(
            envelope.Key,
            first.LeaseToken,
            first.LeaseUntil))).Status.ShouldBe(DurableOutputDeliveryTransitionStatus.LeaseLost);
        var second = (await context.DeliveryStore.TryLeaseAsync(
            Request(first.LeaseUntil, "second"))).ShouldNotBeNull();
        (await context.DeliveryStore.DeadLetterAsync(DeadLetter(
            envelope.Key,
            first.LeaseToken,
            first.LeaseUntil.AddSeconds(1)))).Status
            .ShouldBe(DurableOutputDeliveryTransitionStatus.LeaseLost);
        (await context.DeliveryStore.DeadLetterAsync(DeadLetter(
            envelope.Key,
            second.LeaseToken,
            second.LeasedAt.AddSeconds(1)))).Status
            .ShouldBe(DurableOutputDeliveryTransitionStatus.Applied);

        var details = (await context.DeadLetterStore.GetAsync(envelope.Key)).ShouldNotBeNull();
        details.Envelope.HasSameContent(envelope).ShouldBeTrue();
        details.Attempt.ShouldBe(2);
        details.Generation.ShouldBe(1);
    }

    [Fact]
    public async Task Dead_letter_transition_reports_not_found_or_invalid_state_without_mutation()
    {
        await using var context = await CreateStoreAsync();
        var pending = DurableOutputStoreConformanceData.Envelope("state-pending");
        await context.CaptureStore.EnqueueAsync(pending);
        (await context.DeliveryStore.TryLeaseAsync(Request(
            pending.CapturedAt.AddTicks(-1),
            "initialize-pending"))).ShouldBeNull();
        (await context.DeliveryStore.DeadLetterAsync(DeadLetter(
            pending.Key,
            Guid.NewGuid(),
            Now))).Status.ShouldBe(DurableOutputDeliveryTransitionStatus.InvalidState);

        var completed = DurableOutputStoreConformanceData.Envelope("state-completed");
        await context.CaptureStore.EnqueueAsync(completed);
        var completedLease = (await context.DeliveryStore.TryLeaseAsync(Request(Now)))
            .ShouldNotBeNull();
        completedLease.Envelope.Key.ShouldBe(completed.Key);
        (await context.DeliveryStore.CompleteAsync(new DurableOutputDeliveryTransition(
            completed.Key,
            completedLease.LeaseToken,
            Now.AddSeconds(1)))).Status.ShouldBe(DurableOutputDeliveryTransitionStatus.Applied);
        (await context.DeliveryStore.DeadLetterAsync(DeadLetter(
            completed.Key,
            completedLease.LeaseToken,
            Now.AddSeconds(2)))).Status.ShouldBe(DurableOutputDeliveryTransitionStatus.InvalidState);

        var dead = DurableOutputStoreConformanceData.Envelope("state-dead");
        await context.CaptureStore.EnqueueAsync(dead);
        var deadLease = (await context.DeliveryStore.TryLeaseAsync(Request(Now, "dead")))
            .ShouldNotBeNull();
        deadLease.Envelope.Key.ShouldBe(dead.Key);
        var transition = DeadLetter(dead.Key, deadLease.LeaseToken, Now.AddSeconds(1));
        (await context.DeliveryStore.DeadLetterAsync(transition)).Status
            .ShouldBe(DurableOutputDeliveryTransitionStatus.Applied);
        (await context.DeliveryStore.DeadLetterAsync(transition)).Status
            .ShouldBe(DurableOutputDeliveryTransitionStatus.InvalidState);

        var missing = DurableOutputStoreConformanceData.Envelope("state-missing").Key;
        (await context.DeliveryStore.DeadLetterAsync(DeadLetter(
            missing,
            Guid.NewGuid(),
            Now))).ShouldBe(new DurableOutputDeliveryTransitionResult(
                missing,
                DurableOutputDeliveryTransitionStatus.NotFound));
        (await context.DeadLetterStore.GetAsync(pending.Key)).ShouldBeNull();
        (await context.DeadLetterStore.GetAsync(completed.Key)).ShouldBeNull();
        var details = (await context.DeadLetterStore.GetAsync(dead.Key)).ShouldNotBeNull();
        details.Generation.ShouldBe(1);
        (await context.DeadLetterStore.ListAsync(new DurableOutputDeadLetterQuery()))
            .Items.Select(static item => item.Key).ShouldBe([dead.Key]);
    }

    [Fact]
    public async Task Concurrent_dead_letter_settlements_have_one_applied_winner()
    {
        await using var context = await CreateStoreAsync();
        var envelope = DurableOutputStoreConformanceData.Envelope("concurrent-dead-letter");
        await context.CaptureStore.EnqueueAsync(envelope);
        var lease = (await context.DeliveryStore.TryLeaseAsync(Request(Now)))
            .ShouldNotBeNull();
        var transition = DeadLetter(envelope.Key, lease.LeaseToken, Now.AddSeconds(1));
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var operations = Enumerable.Range(0, 2).Select(_ => Task.Run(async () =>
        {
            await start.Task;
            return await context.DeliveryStore.DeadLetterAsync(transition);
        })).ToArray();

        start.TrySetResult();
        var results = await Task.WhenAll(operations);

        results.Count(static result =>
            result.Status == DurableOutputDeliveryTransitionStatus.Applied).ShouldBe(1);
        results.Count(static result =>
            result.Status == DurableOutputDeliveryTransitionStatus.InvalidState).ShouldBe(1);
        results.ShouldAllBe(result => result.Key == envelope.Key);
        var details = (await context.DeadLetterStore.GetAsync(envelope.Key)).ShouldNotBeNull();
        details.Generation.ShouldBe(1);
        details.Attempt.ShouldBe(1);
    }

    [Fact]
    public async Task List_returns_metadata_only_and_applies_exact_address_reason_and_time_filters()
    {
        await using var context = await CreateStoreAsync();
        var first = DurableOutputStoreConformanceData.Envelope("list-first");
        var second = DurableOutputStoreConformanceData.Copy(
            DurableOutputStoreConformanceData.ErrorEnvelope("list-second"),
            address: DurableOutputStoreConformanceData.SecondaryOutput);
        await CreateDeadLetterAsync(context, first, Now, Now.AddSeconds(1));
        await CreateDeadLetterAsync(
            context,
            second,
            Now.AddMinutes(1),
            Now.AddMinutes(1).AddSeconds(1));

        var all = await context.DeadLetterStore.ListAsync(new DurableOutputDeadLetterQuery());
        all.Items.Select(static item => item.Key).ShouldBe([second.Key, first.Key]);
        var firstSummary = all.Items.Single(item => item.Key == first.Key);
        firstSummary.ContractName.ShouldBe(first.ContractName);
        firstSummary.EnvelopeSchemaVersion.ShouldBe(first.SchemaVersion);
        firstSummary.IsError.ShouldBeFalse();
        ShouldHaveExactTime(firstSummary.CapturedAt, first.CapturedAt);
        firstSummary.Attempt.ShouldBe(1);
        firstSummary.Reason.ShouldBe(DurableOutputDeadLetterReason.HandlerFailure);
        ShouldHaveExactTime(firstSummary.DeadLetteredAt, Now.AddSeconds(1));
        firstSummary.Generation.ShouldBe(1);

        (await context.DeadLetterStore.ListAsync(new DurableOutputDeadLetterQuery(
            address: first.Address))).Items.Select(static item => item.Key).ShouldBe([first.Key]);
        (await context.DeadLetterStore.ListAsync(new DurableOutputDeadLetterQuery(
            reason: DurableOutputDeadLetterReason.HandlerFailure)))
            .Items.Select(static item => item.Key).ShouldBe([second.Key, first.Key]);
        (await context.DeadLetterStore.ListAsync(new DurableOutputDeadLetterQuery(
            address: second.Address,
            reason: DurableOutputDeadLetterReason.HandlerFailure)))
            .Items.Select(static item => item.Key).ShouldBe([second.Key]);
        (await context.DeadLetterStore.ListAsync(new DurableOutputDeadLetterQuery(
            deadLetteredFrom: Now.AddSeconds(1),
            deadLetteredBefore: Now.AddMinutes(1).AddSeconds(1))))
            .Items.Select(static item => item.Key).ShouldBe([first.Key]);
    }

    [Fact]
    public async Task List_uses_default_and_maximum_page_sizes_with_stable_keyset_cursor()
    {
        await using var context = await CreateStoreAsync();
        var envelopes = Enumerable.Range(0, 4)
            .Select(index => DurableOutputStoreConformanceData.Envelope($"page-{index:D2}"))
            .ToArray();
        for (var index = 0; index < envelopes.Length; index++)
        {
            await CreateDeadLetterAsync(
                context,
                envelopes[index],
                Now.AddMinutes(index),
                Now.AddHours(1).AddMinutes(index));
        }

        var first = await context.DeadLetterStore.ListAsync(
            new DurableOutputDeadLetterQuery(pageSize: 2));
        first.Items.Count.ShouldBe(2);
        first.HasMore.ShouldBeTrue();
        first.NextCursor.ShouldNotBeNull().Key.ShouldBe(first.Items[^1].Key);
        ShouldHaveExactTime(first.NextCursor.DeadLetteredAt, first.Items[^1].DeadLetteredAt);
        var second = await context.DeadLetterStore.ListAsync(new DurableOutputDeadLetterQuery(
            cursor: first.NextCursor,
            pageSize: 2));
        second.Items.Count.ShouldBe(2);
        second.HasMore.ShouldBeFalse();
        second.NextCursor.ShouldBeNull();
        first.Items.Concat(second.Items).Select(static item => item.Key)
            .ShouldBe(envelopes.Reverse().Select(static envelope => envelope.Key));
        first.Items.Select(static item => item.Key)
            .Intersect(second.Items.Select(static item => item.Key)).ShouldBeEmpty();
        (await context.DeadLetterStore.ListAsync(new DurableOutputDeadLetterQuery()))
            .Items.Count.ShouldBe(envelopes.Length);
        (await context.DeadLetterStore.ListAsync(new DurableOutputDeadLetterQuery(
            pageSize: DurableOutputDeadLetterQuery.MaximumPageSize)))
            .Items.Count.ShouldBe(envelopes.Length);
    }

    [Fact]
    public async Task List_orders_equal_timestamps_by_binary_address_then_message_id()
    {
        await using var context = await CreateStoreAsync();
        var envelopes = new[]
        {
            DurableOutputStoreConformanceData.Envelope(
                "z-message",
                DurableOutputStoreConformanceData.Output),
            DurableOutputStoreConformanceData.Envelope(
                "a-message",
                DurableOutputStoreConformanceData.SecondaryOutput),
            DurableOutputStoreConformanceData.Envelope(
                "a-message",
                DurableOutputStoreConformanceData.Output)
        };
        foreach (var envelope in envelopes)
            await CreateDeadLetterAsync(context, envelope, Now, Now.AddHours(1));

        var expected = envelopes
            .OrderBy(static envelope => envelope.Address.Value, StringComparer.Ordinal)
            .ThenBy(static envelope => envelope.MessageId.Value, StringComparer.Ordinal)
            .Select(static envelope => envelope.Key)
            .ToArray();
        (await context.DeadLetterStore.ListAsync(new DurableOutputDeadLetterQuery()))
            .Items.Select(static item => item.Key).ShouldBe(expected);

        var paged = new List<DurableOutputKey>();
        DurableOutputDeadLetterCursor? cursor = null;
        do
        {
            var page = await context.DeadLetterStore.ListAsync(new DurableOutputDeadLetterQuery(
                cursor: cursor,
                pageSize: 1));
            paged.AddRange(page.Items.Select(static item => item.Key));
            cursor = page.NextCursor;
        }
        while (cursor is not null);
        paged.ShouldBe(expected);
    }

    [Fact]
    public async Task Get_returns_null_for_missing_or_non_dead_letter_and_exact_complete_envelope()
    {
        await using var context = await CreateStoreAsync();
        var pending = DurableOutputStoreConformanceData.Envelope("lookup-pending");
        var dead = DurableOutputStoreConformanceData.ErrorEnvelope("lookup-dead");
        await context.CaptureStore.EnqueueAsync(pending);
        await CreateDeadLetterAsync(context, dead, Now, Now.AddSeconds(1));

        (await context.DeadLetterStore.GetAsync(pending.Key)).ShouldBeNull();
        (await context.DeadLetterStore.GetAsync(
            DurableOutputStoreConformanceData.Envelope("lookup-absent").Key)).ShouldBeNull();
        var details = (await context.DeadLetterStore.GetAsync(dead.Key)).ShouldNotBeNull();
        details.Envelope.HasSameContent(dead).ShouldBeTrue();
        details.Attempt.ShouldBe(1);
        details.Reason.ShouldBe(DurableOutputDeadLetterReason.HandlerFailure);
        ShouldHaveExactTime(details.DeadLetteredAt, Now.AddSeconds(1));
        details.Generation.ShouldBe(1);
    }

    [Fact]
    public async Task Replay_preserves_envelope_generation_and_schedule_then_next_lease_is_attempt_one()
    {
        await using var context = await CreateStoreAsync();
        var envelope = DurableOutputStoreConformanceData.ErrorEnvelope("replay");
        await CreateDeadLetterAsync(context, envelope, Now, Now.AddSeconds(1));
        var replayedAt = Now.AddMinutes(1);
        var due = replayedAt.AddSeconds(10);

        var result = await context.DeadLetterStore.ReplayAsync(new DurableOutputReplay(
            envelope.Key,
            1,
            replayedAt,
            due));

        result.ShouldBe(new DurableOutputReplayResult(
            envelope.Key,
            DurableOutputReplayStatus.Replayed));
        result.IsReplayed.ShouldBeTrue();
        (await context.DeadLetterStore.GetAsync(envelope.Key)).ShouldBeNull();
        (await context.DeadLetterStore.ListAsync(new DurableOutputDeadLetterQuery()))
            .Items.ShouldBeEmpty();
        (await context.DeliveryStore.TryLeaseAsync(Request(due.AddTicks(-1), "early")))
            .ShouldBeNull();
        var lease = (await context.DeliveryStore.TryLeaseAsync(Request(due, "due")))
            .ShouldNotBeNull();
        lease.Envelope.HasSameContent(envelope).ShouldBeTrue();
        lease.Attempt.ShouldBe(1);
        ShouldHaveExactTime(lease.LeasedAt, due);
    }

    [Fact]
    public async Task Replay_reports_not_found_not_dead_lettered_and_generation_mismatch()
    {
        await using var context = await CreateStoreAsync();
        var pending = DurableOutputStoreConformanceData.Envelope("replay-pending");
        var dead = DurableOutputStoreConformanceData.ErrorEnvelope("replay-dead");
        await context.CaptureStore.EnqueueAsync(pending);
        await CreateDeadLetterAsync(context, dead, Now, Now.AddSeconds(1));
        var at = Now.AddMinutes(1);
        var missing = DurableOutputStoreConformanceData.Envelope("replay-missing").Key;

        (await context.DeadLetterStore.ReplayAsync(new DurableOutputReplay(
            missing,
            1,
            at,
            at))).ShouldBe(new DurableOutputReplayResult(
                missing,
                DurableOutputReplayStatus.NotFound));
        (await context.DeadLetterStore.ReplayAsync(new DurableOutputReplay(
            pending.Key,
            1,
            at,
            at))).ShouldBe(new DurableOutputReplayResult(
                pending.Key,
                DurableOutputReplayStatus.NotDeadLettered));
        (await context.DeadLetterStore.ReplayAsync(new DurableOutputReplay(
            dead.Key,
            2,
            at,
            at))).ShouldBe(new DurableOutputReplayResult(
                dead.Key,
                DurableOutputReplayStatus.GenerationMismatch));
        var details = (await context.DeadLetterStore.GetAsync(dead.Key)).ShouldNotBeNull();
        details.Generation.ShouldBe(1);
        details.Envelope.HasSameContent(dead).ShouldBeTrue();
    }

    [Fact]
    public async Task Stale_generation_cannot_replay_a_later_dead_letter_cycle()
    {
        await using var context = await CreateStoreAsync();
        var envelope = DurableOutputStoreConformanceData.Envelope("generation-cycle");
        await CreateDeadLetterAsync(context, envelope, Now, Now.AddSeconds(1));
        var replayAt = Now.AddMinutes(1);
        (await context.DeadLetterStore.ReplayAsync(new DurableOutputReplay(
            envelope.Key,
            1,
            replayAt,
            replayAt))).Status.ShouldBe(DurableOutputReplayStatus.Replayed);
        var secondLease = (await context.DeliveryStore.TryLeaseAsync(
            Request(replayAt, "cycle-two"))).ShouldNotBeNull();
        (await context.DeliveryStore.DeadLetterAsync(DeadLetter(
            envelope.Key,
            secondLease.LeaseToken,
            replayAt.AddSeconds(1)))).Status.ShouldBe(DurableOutputDeliveryTransitionStatus.Applied);

        var details = (await context.DeadLetterStore.GetAsync(envelope.Key)).ShouldNotBeNull();
        details.Generation.ShouldBe(2);
        details.Attempt.ShouldBe(1);
        (await context.DeadLetterStore.ReplayAsync(new DurableOutputReplay(
            envelope.Key,
            1,
            replayAt.AddMinutes(1),
            replayAt.AddMinutes(1)))).Status.ShouldBe(DurableOutputReplayStatus.GenerationMismatch);
        var retained = (await context.DeadLetterStore.GetAsync(envelope.Key)).ShouldNotBeNull();
        retained.Generation.ShouldBe(2);
        retained.Envelope.HasSameContent(envelope).ShouldBeTrue();
    }

    [Fact]
    public async Task Concurrent_replays_have_one_replayed_winner()
    {
        await using var context = await CreateStoreAsync();
        var envelope = DurableOutputStoreConformanceData.Envelope("concurrent-replay");
        await CreateDeadLetterAsync(context, envelope, Now, Now.AddSeconds(1));
        var replay = new DurableOutputReplay(
            envelope.Key,
            1,
            Now.AddMinutes(1),
            Now.AddMinutes(1));
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var operations = Enumerable.Range(0, 2).Select(_ => Task.Run(async () =>
        {
            await start.Task;
            return await context.DeadLetterStore.ReplayAsync(replay);
        })).ToArray();

        start.TrySetResult();
        var results = await Task.WhenAll(operations);

        results.Count(static result => result.Status == DurableOutputReplayStatus.Replayed)
            .ShouldBe(1);
        results.Count(static result => result.Status == DurableOutputReplayStatus.NotDeadLettered)
            .ShouldBe(1);
        results.ShouldAllBe(result => result.Key == envelope.Key);
        (await context.DeadLetterStore.GetAsync(envelope.Key)).ShouldBeNull();
        var lease = (await context.DeliveryStore.TryLeaseAsync(
            Request(Now.AddMinutes(1), "after-replay"))).ShouldNotBeNull();
        lease.Envelope.HasSameContent(envelope).ShouldBeTrue();
        lease.Attempt.ShouldBe(1);
    }

    [Fact]
    public async Task Precancelled_dead_letter_list_get_and_replay_do_not_mutate_state()
    {
        await using var context = await CreateStoreAsync();
        var envelope = DurableOutputStoreConformanceData.ErrorEnvelope("cancel-dead-letter");
        await CreateDeadLetterAsync(context, envelope, Now, Now.AddSeconds(1));
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        var replay = new DurableOutputReplay(
            envelope.Key,
            1,
            Now.AddMinutes(1),
            Now.AddMinutes(1));

        await Should.ThrowAsync<OperationCanceledException>(() =>
            context.DeadLetterStore.ListAsync(
                new DurableOutputDeadLetterQuery(),
                canceled.Token).AsTask());
        await Should.ThrowAsync<OperationCanceledException>(() =>
            context.DeadLetterStore.GetAsync(envelope.Key, canceled.Token).AsTask());
        await Should.ThrowAsync<OperationCanceledException>(() =>
            context.DeadLetterStore.ReplayAsync(replay, canceled.Token).AsTask());
        var retained = (await context.DeadLetterStore.GetAsync(envelope.Key)).ShouldNotBeNull();
        retained.Generation.ShouldBe(1);
        retained.Envelope.HasSameContent(envelope).ShouldBeTrue();

        var active = DurableOutputStoreConformanceData.Envelope("cancel-settlement");
        await context.CaptureStore.EnqueueAsync(active);
        var lease = (await context.DeliveryStore.TryLeaseAsync(Request(Now, "cancel-owner")))
            .ShouldNotBeNull();
        var transition = DeadLetter(active.Key, lease.LeaseToken, Now.AddSeconds(1));
        await Should.ThrowAsync<OperationCanceledException>(() =>
            context.DeliveryStore.DeadLetterAsync(transition, canceled.Token).AsTask());
        (await context.DeliveryStore.DeadLetterAsync(transition)).Status
            .ShouldBe(DurableOutputDeliveryTransitionStatus.Applied);
        (await context.DeadLetterStore.GetAsync(active.Key)).ShouldNotBeNull().Generation.ShouldBe(1);
    }

    private static async ValueTask<DurableOutputDeliveryTransitionResult> CreateDeadLetterAsync(
        DurableOutputDeadLetterStoreTestContext context,
        DurableOutputEnvelope envelope,
        DateTimeOffset leaseAt,
        DateTimeOffset deadLetteredAt)
    {
        (await context.CaptureStore.EnqueueAsync(envelope)).Status
            .ShouldBe(DurableOutputEnqueueStatus.Enqueued);
        var lease = (await context.DeliveryStore.TryLeaseAsync(Request(leaseAt)))
            .ShouldNotBeNull();
        lease.Envelope.Key.ShouldBe(envelope.Key);
        return await context.DeliveryStore.DeadLetterAsync(DeadLetter(
            envelope.Key,
            lease.LeaseToken,
            deadLetteredAt));
    }

    private static DurableOutputDeliveryDeadLetter DeadLetter(
        DurableOutputKey key,
        Guid leaseToken,
        DateTimeOffset deadLetteredAt)
        => DurableOutputStoreConformanceData.DeadLetter(key, leaseToken, deadLetteredAt);

    private static DurableOutputDeliveryLeaseRequest Request(
        DateTimeOffset now,
        string ownerId = "dead-letter-worker")
        => DurableOutputStoreConformanceData.DeliveryRequest(
            now,
            ownerId,
            TimeSpan.FromDays(1));

    private static void ShouldHaveExactTime(DateTimeOffset actual, DateTimeOffset expected)
    {
        actual.UtcTicks.ShouldBe(expected.UtcTicks);
        actual.Offset.ShouldBe(expected.Offset);
    }
}
