using Shouldly;
using Xunit;

namespace FluxFlow.Engine.DurableInput.Tests;

/// <summary>
/// Provider-observable inspection and replay conformance specification.
/// </summary>
public abstract class DurableInputDeadLetterStoreConformanceTests
{
    protected abstract ValueTask<DurableInputDeadLetterStoreTestContext> CreateStoreAsync();

    [Fact]
    public async Task List_filters_current_dead_letters_and_uses_inclusive_from_exclusive_before()
    {
        await using var context = await CreateStoreAsync();
        var first = DurableInputStoreConformanceData.Envelope("filter-first");
        var middle = DurableInputStoreConformanceData.Envelope("filter-middle");
        var last = DurableInputStoreConformanceData.Envelope(
            "filter-last",
            address: DurableInputStoreConformanceData.SecondaryInput);
        var firstAt = DurableInputStoreConformanceData.Now.AddMinutes(-3);
        var middleAt = DurableInputStoreConformanceData.Now.AddMinutes(-2);
        var lastAt = DurableInputStoreConformanceData.Now.AddMinutes(-1);
        await DeadLetterAsync(
            context,
            first,
            firstAt,
            DurableInputFailureKind.UnknownContract);
        await DeadLetterAsync(
            context,
            middle,
            middleAt,
            DurableInputFailureKind.InvalidEnvelope);
        await DeadLetterAsync(
            context,
            last,
            lastAt,
            DurableInputFailureKind.UnknownContract);

        var byAddress = await context.DeadLetters.ListAsync(new DurableInputDeadLetterQuery(
            address: DurableInputStoreConformanceData.Input));
        var byFailure = await context.DeadLetters.ListAsync(new DurableInputDeadLetterQuery(
            failureKind: DurableInputFailureKind.UnknownContract));
        var bounded = await context.DeadLetters.ListAsync(new DurableInputDeadLetterQuery(
            deadLetteredFrom: middleAt,
            deadLetteredBefore: lastAt));

        byAddress.Items.Select(static item => item.Key).ShouldBe([middle.Key, first.Key]);
        byFailure.Items.Select(static item => item.Key).ShouldBe([last.Key, first.Key]);
        bounded.Items.ShouldHaveSingleItem().Key.ShouldBe(middle.Key);
        byAddress.NextCursor.ShouldBeNull();
        byFailure.NextCursor.ShouldBeNull();
        bounded.NextCursor.ShouldBeNull();
    }

    [Fact]
    public async Task List_orders_newest_first_with_stable_key_ties_and_keyset_pages_without_gaps()
    {
        await using var context = await CreateStoreAsync();
        var newest = DurableInputStoreConformanceData.Envelope("page-newest");
        var tiedA = DurableInputStoreConformanceData.Envelope("page-a");
        var tiedB = DurableInputStoreConformanceData.Envelope("page-b");
        var tiedC = DurableInputStoreConformanceData.Envelope("page-c");
        var tiedSecondary = DurableInputStoreConformanceData.Envelope(
            "page-a",
            address: DurableInputStoreConformanceData.SecondaryInput);
        var oldest = DurableInputStoreConformanceData.Envelope("page-oldest");
        var tiedAt = DurableInputStoreConformanceData.Now;
        await DeadLetterAsync(context, tiedC, tiedAt);
        await DeadLetterAsync(context, oldest, tiedAt.AddMinutes(-1));
        await DeadLetterAsync(context, tiedA, tiedAt);
        await DeadLetterAsync(context, newest, tiedAt.AddMinutes(1));
        await DeadLetterAsync(context, tiedB, tiedAt);
        await DeadLetterAsync(context, tiedSecondary, tiedAt);

        var first = await context.DeadLetters.ListAsync(
            new DurableInputDeadLetterQuery(pageSize: 2));
        var second = await context.DeadLetters.ListAsync(
            new DurableInputDeadLetterQuery(cursor: first.NextCursor, pageSize: 2));
        var third = await context.DeadLetters.ListAsync(
            new DurableInputDeadLetterQuery(cursor: second.NextCursor, pageSize: 2));
        var allKeys = first.Items
            .Concat(second.Items)
            .Concat(third.Items)
            .Select(static item => item.Key)
            .ToArray();

        first.Items.Select(static item => item.Key).ShouldBe([newest.Key, tiedA.Key]);
        first.NextCursor.ShouldBe(new DurableInputDeadLetterCursor(tiedAt, tiedA.Key));
        first.HasMore.ShouldBeTrue();
        second.Items.Select(static item => item.Key).ShouldBe([tiedB.Key, tiedC.Key]);
        second.NextCursor.ShouldBe(new DurableInputDeadLetterCursor(tiedAt, tiedC.Key));
        second.HasMore.ShouldBeTrue();
        third.Items.Select(static item => item.Key).ShouldBe([tiedSecondary.Key, oldest.Key]);
        third.NextCursor.ShouldBeNull();
        third.HasMore.ShouldBeFalse();
        allKeys.ShouldBe([
            newest.Key,
            tiedA.Key,
            tiedB.Key,
            tiedC.Key,
            tiedSecondary.Key,
            oldest.Key
        ]);
        allKeys.Distinct().Count().ShouldBe(6);
    }

    [Fact]
    public async Task List_returns_payload_free_summaries_with_exact_operational_metadata()
    {
        await using var context = await CreateStoreAsync();
        const string secretPayload = "private-payload-sentinel";
        const string secretHeader = "private-header-sentinel";
        var envelope = DurableInputStoreConformanceData.Envelope(
            "summary-metadata",
            secretPayload,
            contractName: "orders-v7",
            headers: new Dictionary<string, string> { ["secret"] = secretHeader },
            schemaVersion: 7);
        var failure = DurableInputStoreConformanceData.Failure(
            DurableInputFailureKind.PayloadTypeMismatch,
            "registered input type did not match");
        var deadLetteredAt = DurableInputStoreConformanceData.Now.AddMinutes(1);
        await DeadLetterAsync(context, envelope, deadLetteredAt, failure: failure);

        var summary = (await context.DeadLetters.ListAsync(new DurableInputDeadLetterQuery()))
            .Items
            .ShouldHaveSingleItem();

        summary.Key.ShouldBe(envelope.Key);
        summary.ContractName.ShouldBe(envelope.ContractName);
        summary.EnvelopeSchemaVersion.ShouldBe(7);
        summary.IsError.ShouldBeFalse();
        summary.EnqueuedAt.ShouldBe(envelope.EnqueuedAt);
        summary.Attempt.ShouldBe(1);
        summary.FailureKind.ShouldBe(failure.Kind);
        summary.DeadLetteredAt.ShouldBe(deadLetteredAt);
        summary.Generation.ShouldBe(1);
        summary.ToString().ShouldNotContain(secretPayload);
        summary.ToString().ShouldNotContain(secretHeader);
        summary.ToString().ShouldNotContain(failure.Description);
    }

    [Fact]
    public async Task Get_returns_the_exact_full_envelope_and_current_dead_letter_metadata()
    {
        await using var context = await CreateStoreAsync();
        var envelope = DurableInputStoreConformanceData.Envelope(
            "full-details",
            "detailed-payload",
            timestamp: DurableInputStoreConformanceData.Now.AddHours(-2),
            enqueuedAt: DurableInputStoreConformanceData.Now.AddHours(-1),
            contractName: "details-v4",
            traceId: "details-trace",
            correlationId: "details-correlation",
            causationId: "details-cause",
            headers: new Dictionary<string, string>
            {
                ["Tenant"] = "North",
                ["source"] = "conformance"
            },
            schemaVersion: 4);
        var failure = DurableInputStoreConformanceData.Failure(
            DurableInputFailureKind.DeserializationFailed,
            "cannot restore details-v4");
        var deadLetteredAt = DurableInputStoreConformanceData.Now.AddMinutes(2);
        await DeadLetterAsync(context, envelope, deadLetteredAt, failure: failure);

        var details = await context.DeadLetters.GetAsync(envelope.Key);

        details.ShouldNotBeNull();
        ShouldMatchEnvelope(details.Envelope, envelope);
        details.Attempt.ShouldBe(1);
        details.Failure.ShouldBe(failure);
        details.DeadLetteredAt.ShouldBe(deadLetteredAt);
        details.Generation.ShouldBe(1);
    }

    [Fact]
    public async Task Get_returns_null_for_missing_pending_leased_delivered_and_replayed_records()
    {
        await using var context = await CreateStoreAsync();
        var missing = DurableInputStoreConformanceData.Envelope("lookup-missing");
        var pending = DurableInputStoreConformanceData.Envelope(
            "lookup-pending",
            enqueuedAt: DurableInputStoreConformanceData.Now.AddDays(1));
        var leased = DurableInputStoreConformanceData.Envelope("lookup-leased");
        var delivered = DurableInputStoreConformanceData.Envelope("lookup-delivered");
        var replayed = DurableInputStoreConformanceData.Envelope("lookup-replayed");
        await context.Store.EnqueueAsync(pending);
        await context.Store.EnqueueAsync(leased);
        var leasedLease = (await context.Store.LeaseAsync(
            DurableInputStoreConformanceData.Request(maxCount: 1))).Single();
        leasedLease.Envelope.Key.ShouldBe(leased.Key);
        await context.Store.EnqueueAsync(delivered);
        var deliveredLease = (await context.Store.LeaseAsync(
            DurableInputStoreConformanceData.Request(maxCount: 1))).Single();
        deliveredLease.Envelope.Key.ShouldBe(delivered.Key);
        (await context.Store.MarkDeliveredAsync(new DurableInputLeaseTransition(
            delivered.Key,
            deliveredLease.LeaseToken,
            DurableInputStoreConformanceData.Now.AddSeconds(1))))
            .Status.ShouldBe(DurableInputTransitionStatus.Applied);
        await DeadLetterAsync(context, replayed, DurableInputStoreConformanceData.Now.AddSeconds(2));
        (await context.DeadLetters.ReplayAsync(new DurableInputReplay(
            replayed.Key,
            1,
            DurableInputStoreConformanceData.Now.AddSeconds(3),
            DurableInputStoreConformanceData.Now.AddMinutes(1))))
            .Status.ShouldBe(DurableInputReplayStatus.Replayed);

        (await context.DeadLetters.GetAsync(missing.Key)).ShouldBeNull();
        (await context.DeadLetters.GetAsync(pending.Key)).ShouldBeNull();
        (await context.DeadLetters.GetAsync(leased.Key)).ShouldBeNull();
        (await context.DeadLetters.GetAsync(delivered.Key)).ShouldBeNull();
        (await context.DeadLetters.GetAsync(replayed.Key)).ShouldBeNull();
    }

    [Fact]
    public async Task Replay_returns_not_found_not_dead_lettered_and_generation_mismatch_without_mutation()
    {
        await using var context = await CreateStoreAsync();
        var missing = DurableInputStoreConformanceData.Envelope("replay-missing");
        var pending = DurableInputStoreConformanceData.Envelope(
            "replay-pending",
            enqueuedAt: DurableInputStoreConformanceData.Now.AddDays(1));
        var delivered = DurableInputStoreConformanceData.Envelope("replay-delivered");
        var dead = DurableInputStoreConformanceData.Envelope("replay-generation");
        await context.Store.EnqueueAsync(pending);
        await context.Store.EnqueueAsync(delivered);
        var deliveredLease = (await context.Store.LeaseAsync(
            DurableInputStoreConformanceData.Request())).Single();
        deliveredLease.Envelope.Key.ShouldBe(delivered.Key);
        (await context.Store.MarkDeliveredAsync(new DurableInputLeaseTransition(
            delivered.Key,
            deliveredLease.LeaseToken,
            DurableInputStoreConformanceData.Now.AddSeconds(1))))
            .Status.ShouldBe(DurableInputTransitionStatus.Applied);
        await DeadLetterAsync(context, dead, DurableInputStoreConformanceData.Now);
        var before = await context.DeadLetters.GetAsync(dead.Key);

        var missingResult = await context.DeadLetters.ReplayAsync(Replay(missing.Key, 1));
        var pendingResult = await context.DeadLetters.ReplayAsync(Replay(pending.Key, 1));
        var deliveredResult = await context.DeadLetters.ReplayAsync(Replay(delivered.Key, 1));
        var mismatchResult = await context.DeadLetters.ReplayAsync(Replay(dead.Key, 2));
        var after = await context.DeadLetters.GetAsync(dead.Key);

        missingResult.Status.ShouldBe(DurableInputReplayStatus.NotFound);
        pendingResult.Status.ShouldBe(DurableInputReplayStatus.NotDeadLettered);
        deliveredResult.Status.ShouldBe(DurableInputReplayStatus.NotDeadLettered);
        mismatchResult.Status.ShouldBe(DurableInputReplayStatus.GenerationMismatch);
        missingResult.IsReplayed.ShouldBeFalse();
        pendingResult.IsReplayed.ShouldBeFalse();
        deliveredResult.IsReplayed.ShouldBeFalse();
        mismatchResult.IsReplayed.ShouldBeFalse();
        before.ShouldNotBeNull();
        after.ShouldNotBeNull();
        ShouldMatchDetails(after, before);
        (await context.Store.LeaseAsync(DurableInputStoreConformanceData.Request(
            now: pending.EnqueuedAt,
            leaseUntil: pending.EnqueuedAt.AddMinutes(1))))
            .ShouldHaveSingleItem()
            .Envelope.Key.ShouldBe(pending.Key);
    }

    [Fact]
    public async Task Replay_preserves_envelope_resets_operational_state_and_increments_generation()
    {
        await using var context = await CreateStoreAsync();
        var envelope = DurableInputStoreConformanceData.Envelope(
            "replay-reset",
            "preserved-payload",
            headers: new Dictionary<string, string> { ["preserved"] = "header" });
        var originalFailure = DurableInputStoreConformanceData.Failure(
            DurableInputFailureKind.InvalidEnvelope,
            "original failure");
        await DeadLetterAsync(
            context,
            envelope,
            DurableInputStoreConformanceData.Now,
            failure: originalFailure);
        var scheduledAt = DurableInputStoreConformanceData.Now.AddMinutes(5);

        var replayed = await context.DeadLetters.ReplayAsync(new DurableInputReplay(
            envelope.Key,
            1,
            DurableInputStoreConformanceData.Now.AddMinutes(1),
            scheduledAt));
        var beforeSchedule = await context.Store.LeaseAsync(DurableInputStoreConformanceData.Request(
            now: scheduledAt.AddTicks(-1),
            leaseUntil: scheduledAt.AddMinutes(1)));
        var lease = (await context.Store.LeaseAsync(DurableInputStoreConformanceData.Request(
            now: scheduledAt,
            leaseUntil: scheduledAt.AddMinutes(1)))).Single();

        replayed.Status.ShouldBe(DurableInputReplayStatus.Replayed);
        replayed.IsReplayed.ShouldBeTrue();
        (await context.DeadLetters.GetAsync(envelope.Key)).ShouldBeNull();
        (await context.DeadLetters.ListAsync(new DurableInputDeadLetterQuery())).Items.ShouldBeEmpty();
        beforeSchedule.ShouldBeEmpty();
        lease.Attempt.ShouldBe(1);
        ShouldMatchEnvelope(lease.Envelope, envelope);

        var secondFailure = DurableInputStoreConformanceData.Failure(
            DurableInputFailureKind.MaximumAttemptsExceeded,
            "second generation failure");
        (await context.Store.DeadLetterAsync(new DurableInputDeadLetter(
            envelope.Key,
            lease.LeaseToken,
            scheduledAt.AddSeconds(1),
            secondFailure))).Status.ShouldBe(DurableInputTransitionStatus.Applied);
        var secondGeneration = await context.DeadLetters.GetAsync(envelope.Key);
        secondGeneration.ShouldNotBeNull();
        secondGeneration.Generation.ShouldBe(2);
        secondGeneration.Attempt.ShouldBe(1);
        secondGeneration.Failure.ShouldBe(secondFailure);
        secondGeneration.DeadLetteredAt.ShouldBe(scheduledAt.AddSeconds(1));
        ShouldMatchEnvelope(secondGeneration.Envelope, envelope);

        var staleReplay = await context.DeadLetters.ReplayAsync(new DurableInputReplay(
            envelope.Key,
            expectedGeneration: 1,
            scheduledAt.AddSeconds(2),
            scheduledAt.AddSeconds(2)));
        var unchanged = await context.DeadLetters.GetAsync(envelope.Key);

        staleReplay.Status.ShouldBe(DurableInputReplayStatus.GenerationMismatch);
        staleReplay.IsReplayed.ShouldBeFalse();
        unchanged.ShouldNotBeNull();
        ShouldMatchDetails(unchanged, secondGeneration);
    }

    [Fact]
    public async Task Precancelled_list_get_and_replay_do_not_mutate_state()
    {
        await using var context = await CreateStoreAsync();
        var envelope = DurableInputStoreConformanceData.Envelope("cancelled-operations");
        await DeadLetterAsync(context, envelope, DurableInputStoreConformanceData.Now);
        var before = await context.DeadLetters.GetAsync(envelope.Key);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(() =>
            context.DeadLetters.ListAsync(
                new DurableInputDeadLetterQuery(),
                cancellation.Token).AsTask());
        await Should.ThrowAsync<OperationCanceledException>(() =>
            context.DeadLetters.GetAsync(envelope.Key, cancellation.Token).AsTask());
        await Should.ThrowAsync<OperationCanceledException>(() =>
            context.DeadLetters.ReplayAsync(
                Replay(envelope.Key, 1),
                cancellation.Token).AsTask());

        var after = await context.DeadLetters.GetAsync(envelope.Key);
        before.ShouldNotBeNull();
        after.ShouldNotBeNull();
        ShouldMatchDetails(after, before);
        (await context.DeadLetters.ListAsync(new DurableInputDeadLetterQuery()))
            .Items.ShouldHaveSingleItem().Key.ShouldBe(envelope.Key);
    }

    private static DurableInputReplay Replay(DurableInputKey key, long generation)
        => new(
            key,
            generation,
            DurableInputStoreConformanceData.Now.AddMinutes(1),
            DurableInputStoreConformanceData.Now.AddMinutes(2));

    private static async ValueTask DeadLetterAsync(
        DurableInputDeadLetterStoreTestContext context,
        DurableInputEnvelope envelope,
        DateTimeOffset deadLetteredAt,
        DurableInputFailureKind failureKind = DurableInputFailureKind.InvalidEnvelope,
        DurableInputFailure? failure = null)
    {
        (await context.Store.EnqueueAsync(envelope))
            .Status.ShouldBe(DurableInputEnqueueStatus.Enqueued);
        var leaseUntilBase = envelope.EnqueuedAt > deadLetteredAt
            ? envelope.EnqueuedAt
            : deadLetteredAt;
        var lease = (await context.Store.LeaseAsync(DurableInputStoreConformanceData.Request(
            now: envelope.EnqueuedAt,
            leaseUntil: leaseUntilBase.AddMinutes(10)))).Single();
        lease.Envelope.Key.ShouldBe(envelope.Key);
        (await context.Store.DeadLetterAsync(new DurableInputDeadLetter(
            envelope.Key,
            lease.LeaseToken,
            deadLetteredAt,
            failure ?? DurableInputStoreConformanceData.Failure(failureKind))))
            .Status.ShouldBe(DurableInputTransitionStatus.Applied);
    }

    private static void ShouldMatchEnvelope(
        DurableInputEnvelope actual,
        DurableInputEnvelope expected)
    {
        actual.HasSameContent(expected).ShouldBeTrue();
        actual.Key.ShouldBe(expected.Key);
        actual.EnqueuedAt.ShouldBe(expected.EnqueuedAt);
        actual.Timestamp.ShouldBe(expected.Timestamp);
        actual.Headers.ShouldBe(expected.Headers);
        actual.Payload.GetRawText().ShouldBe(expected.Payload.GetRawText());
    }

    private static void ShouldMatchDetails(
        DurableInputDeadLetterDetails actual,
        DurableInputDeadLetterDetails expected)
    {
        ShouldMatchEnvelope(actual.Envelope, expected.Envelope);
        actual.Attempt.ShouldBe(expected.Attempt);
        actual.Failure.ShouldBe(expected.Failure);
        actual.DeadLetteredAt.ShouldBe(expected.DeadLetteredAt);
        actual.Generation.ShouldBe(expected.Generation);
    }
}
