using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.DurableInput.Tests;

/// <summary>
/// Executable, provider-observable conformance specification for
/// <see cref="IDurableInputStore"/> implementations.
/// </summary>
public abstract class DurableInputStoreConformanceTests
{
    protected abstract ValueTask<DurableInputStoreTestContext> CreateStoreAsync();

    [Fact]
    public async Task Enqueue_is_idempotent_by_address_and_message_id_without_overwrite()
    {
        await using var context = await CreateStoreAsync();
        var store = context.Store;
        var original = DurableInputStoreConformanceData.Envelope(
            enqueuedAt: DurableInputStoreConformanceData.Now.AddMinutes(-2));
        var equivalent = DurableInputStoreConformanceData.Envelope(
            enqueuedAt: DurableInputStoreConformanceData.Now.AddMinutes(-1));
        var conflict = DurableInputStoreConformanceData.Envelope(
            value: "changed",
            enqueuedAt: DurableInputStoreConformanceData.Now.AddMinutes(-1));

        var firstResult = await store.EnqueueAsync(original);
        var equivalentResult = await store.EnqueueAsync(equivalent);
        var conflictResult = await store.EnqueueAsync(conflict);
        var lease = (await store.LeaseAsync(DurableInputStoreConformanceData.Request())).Single();

        firstResult.ShouldBe(new DurableInputEnqueueResult(
            original.Key,
            DurableInputEnqueueStatus.Enqueued));
        equivalentResult.ShouldBe(new DurableInputEnqueueResult(
            original.Key,
            DurableInputEnqueueStatus.AlreadyExists));
        conflictResult.ShouldBe(new DurableInputEnqueueResult(
            original.Key,
            DurableInputEnqueueStatus.Conflict));
        lease.Envelope.HasSameContent(original).ShouldBeTrue();
        lease.Envelope.EnqueuedAt.ShouldBe(original.EnqueuedAt);
        lease.Envelope.Payload.GetString().ShouldBe("payload");
    }

    [Fact]
    public async Task Enqueue_scopes_the_same_message_id_to_each_application_address()
    {
        await using var context = await CreateStoreAsync();
        var store = context.Store;
        var primary = DurableInputStoreConformanceData.Envelope();
        var secondary = DurableInputStoreConformanceData.Envelope(
            address: DurableInputStoreConformanceData.SecondaryInput);

        var primaryResult = await store.EnqueueAsync(primary);
        var secondaryResult = await store.EnqueueAsync(secondary);
        var leases = await store.LeaseAsync(DurableInputStoreConformanceData.Request(maxCount: 2));

        primaryResult.Status.ShouldBe(DurableInputEnqueueStatus.Enqueued);
        secondaryResult.Status.ShouldBe(DurableInputEnqueueStatus.Enqueued);
        leases.Select(static lease => lease.Envelope.Key)
            .ShouldBe([primary.Key, secondary.Key], ignoreOrder: true);
    }

    [Fact]
    public async Task Lease_orders_released_expired_and_tied_records_and_respects_max_count()
    {
        await using var context = await CreateStoreAsync();
        var store = context.Store;
        var released = DurableInputStoreConformanceData.Envelope(
            messageId: "message-released",
            enqueuedAt: DurableInputStoreConformanceData.Now.AddMinutes(-10));
        await store.EnqueueAsync(released);
        var releasedLease = (await store.LeaseAsync(DurableInputStoreConformanceData.Request(
            now: DurableInputStoreConformanceData.Now.AddMinutes(-9),
            leaseUntil: DurableInputStoreConformanceData.Now.AddMinutes(-8)))).Single();
        var releaseResult = await store.ReleaseAsync(new DurableInputRelease(
            released.Key,
            releasedLease.LeaseToken,
            DurableInputStoreConformanceData.Now.AddMinutes(-8).AddSeconds(-1),
            DurableInputStoreConformanceData.Now.AddMinutes(-3),
            DurableInputStoreConformanceData.Failure()));

        var expired = DurableInputStoreConformanceData.Envelope(
            messageId: "message-expired",
            enqueuedAt: DurableInputStoreConformanceData.Now.AddMinutes(-5));
        await store.EnqueueAsync(expired);
        var expiredLease = (await store.LeaseAsync(DurableInputStoreConformanceData.Request(
            now: DurableInputStoreConformanceData.Now.AddMinutes(-5),
            leaseUntil: DurableInputStoreConformanceData.Now.AddMinutes(-2)))).Single();

        var tiedA = DurableInputStoreConformanceData.Envelope(
            messageId: "message-a",
            enqueuedAt: DurableInputStoreConformanceData.Now.AddMinutes(-1));
        var tiedB = DurableInputStoreConformanceData.Envelope(
            messageId: "message-b",
            enqueuedAt: DurableInputStoreConformanceData.Now.AddMinutes(-1));
        await store.EnqueueAsync(tiedB);
        await store.EnqueueAsync(tiedA);

        var firstPage = await store.LeaseAsync(DurableInputStoreConformanceData.Request(
            ownerId: "owner-current",
            maxCount: 3));
        var secondPage = await store.LeaseAsync(DurableInputStoreConformanceData.Request(
            ownerId: "owner-current",
            maxCount: 3));

        releaseResult.Status.ShouldBe(DurableInputTransitionStatus.Applied);
        firstPage.Select(static lease => lease.Envelope.MessageId).ShouldBe([
            released.MessageId,
            expired.MessageId,
            tiedA.MessageId
        ]);
        firstPage.Select(static lease => lease.Attempt).ShouldBe([2, 2, 1]);
        firstPage.ShouldAllBe(static lease =>
            lease.OwnerId == "owner-current" &&
            lease.LeasedAt == DurableInputStoreConformanceData.Now &&
            lease.LeaseUntil == DurableInputStoreConformanceData.Now.AddSeconds(30) &&
            lease.LeaseToken != Guid.Empty);
        firstPage.Select(static lease => lease.LeaseToken).Distinct().Count().ShouldBe(3);
        secondPage.ShouldHaveSingleItem().Envelope.Key.ShouldBe(tiedB.Key);
        expiredLease.LeaseToken.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task Lease_is_exclusive_until_expiry_then_renews_with_a_new_token_and_attempt()
    {
        await using var context = await CreateStoreAsync();
        var store = context.Store;
        var envelope = DurableInputStoreConformanceData.Envelope();
        await store.EnqueueAsync(envelope);

        var first = (await store.LeaseAsync(DurableInputStoreConformanceData.Request(
            ownerId: "owner-a"))).Single();
        var competing = await store.LeaseAsync(DurableInputStoreConformanceData.Request(
            ownerId: "owner-b",
            now: DurableInputStoreConformanceData.Now.AddSeconds(29),
            leaseUntil: DurableInputStoreConformanceData.Now.AddMinutes(1)));
        var renewed = (await store.LeaseAsync(DurableInputStoreConformanceData.Request(
            ownerId: "owner-b",
            now: first.LeaseUntil,
            leaseUntil: first.LeaseUntil.AddSeconds(30)))).Single();

        first.OwnerId.ShouldBe("owner-a");
        first.Attempt.ShouldBe(1);
        competing.ShouldBeEmpty();
        renewed.Envelope.Key.ShouldBe(envelope.Key);
        renewed.OwnerId.ShouldBe("owner-b");
        renewed.Attempt.ShouldBe(2);
        renewed.LeaseToken.ShouldNotBe(first.LeaseToken);
    }

    [Fact]
    public async Task Release_defers_retry_until_the_explicit_next_attempt_time()
    {
        await using var context = await CreateStoreAsync();
        var store = context.Store;
        var envelope = DurableInputStoreConformanceData.Envelope();
        await store.EnqueueAsync(envelope);
        var lease = (await store.LeaseAsync(DurableInputStoreConformanceData.Request())).Single();
        var dueAt = DurableInputStoreConformanceData.Now.AddMinutes(2);

        var released = await store.ReleaseAsync(new DurableInputRelease(
            envelope.Key,
            lease.LeaseToken,
            DurableInputStoreConformanceData.Now.AddSeconds(1),
            dueAt,
            DurableInputStoreConformanceData.Failure(
                DurableInputFailureKind.InputFull,
                "input remained full")));
        var beforeDue = await store.LeaseAsync(DurableInputStoreConformanceData.Request(
            now: dueAt.AddTicks(-1),
            leaseUntil: dueAt.AddSeconds(30)));
        var atDue = (await store.LeaseAsync(DurableInputStoreConformanceData.Request(
            now: dueAt,
            leaseUntil: dueAt.AddSeconds(30)))).Single();

        released.Status.ShouldBe(DurableInputTransitionStatus.Applied);
        beforeDue.ShouldBeEmpty();
        atDue.Envelope.Key.ShouldBe(envelope.Key);
        atDue.Attempt.ShouldBe(2);
    }

    [Fact]
    public async Task Transitions_reject_wrong_expired_and_stale_tokens_without_mutating_the_current_lease()
    {
        await using var context = await CreateStoreAsync();
        var store = context.Store;
        var envelope = DurableInputStoreConformanceData.Envelope();
        await store.EnqueueAsync(envelope);
        var first = (await store.LeaseAsync(DurableInputStoreConformanceData.Request(
            ownerId: "owner-a"))).Single();

        var wrongToken = await store.MarkDeliveredAsync(new DurableInputLeaseTransition(
            envelope.Key,
            Guid.NewGuid(),
            DurableInputStoreConformanceData.Now.AddSeconds(1)));
        var exactExpiry = await store.MarkDeliveredAsync(new DurableInputLeaseTransition(
            envelope.Key,
            first.LeaseToken,
            first.LeaseUntil));
        var renewed = (await store.LeaseAsync(DurableInputStoreConformanceData.Request(
            ownerId: "owner-b",
            now: first.LeaseUntil,
            leaseUntil: first.LeaseUntil.AddMinutes(1)))).Single();
        var staleToken = await store.DeadLetterAsync(new DurableInputDeadLetter(
            envelope.Key,
            first.LeaseToken,
            first.LeaseUntil.AddSeconds(1),
            DurableInputStoreConformanceData.Failure(DurableInputFailureKind.UnknownContract)));
        var applied = await store.ReleaseAsync(new DurableInputRelease(
            envelope.Key,
            renewed.LeaseToken,
            first.LeaseUntil.AddSeconds(2),
            first.LeaseUntil.AddMinutes(2),
            DurableInputStoreConformanceData.Failure()));
        var repeated = await store.MarkDeliveredAsync(new DurableInputLeaseTransition(
            envelope.Key,
            renewed.LeaseToken,
            first.LeaseUntil.AddSeconds(3)));
        var beforeDue = await store.LeaseAsync(DurableInputStoreConformanceData.Request(
            now: first.LeaseUntil.AddMinutes(2).AddTicks(-1),
            leaseUntil: first.LeaseUntil.AddMinutes(3)));

        wrongToken.Status.ShouldBe(DurableInputTransitionStatus.LeaseLost);
        exactExpiry.Status.ShouldBe(DurableInputTransitionStatus.LeaseLost);
        renewed.OwnerId.ShouldBe("owner-b");
        renewed.Attempt.ShouldBe(2);
        renewed.LeaseToken.ShouldNotBe(first.LeaseToken);
        staleToken.Status.ShouldBe(DurableInputTransitionStatus.LeaseLost);
        applied.Status.ShouldBe(DurableInputTransitionStatus.Applied);
        repeated.Status.ShouldBe(DurableInputTransitionStatus.InvalidState);
        beforeDue.ShouldBeEmpty();
    }

    [Fact]
    public async Task Transitions_return_not_found_for_every_missing_key_operation()
    {
        await using var context = await CreateStoreAsync();
        var store = context.Store;
        var missing = DurableInputStoreConformanceData.Envelope(messageId: "missing").Key;
        var token = Guid.NewGuid();

        var delivered = await store.MarkDeliveredAsync(new DurableInputLeaseTransition(
            missing,
            token,
            DurableInputStoreConformanceData.Now));
        var released = await store.ReleaseAsync(new DurableInputRelease(
            missing,
            token,
            DurableInputStoreConformanceData.Now,
            DurableInputStoreConformanceData.Now,
            DurableInputStoreConformanceData.Failure()));
        var deadLettered = await store.DeadLetterAsync(new DurableInputDeadLetter(
            missing,
            token,
            DurableInputStoreConformanceData.Now,
            DurableInputStoreConformanceData.Failure()));

        delivered.Status.ShouldBe(DurableInputTransitionStatus.NotFound);
        released.Status.ShouldBe(DurableInputTransitionStatus.NotFound);
        deadLettered.Status.ShouldBe(DurableInputTransitionStatus.NotFound);
    }

    [Fact]
    public async Task Delivered_and_dead_lettered_records_remain_terminal_idempotency_tombstones()
    {
        await using var context = await CreateStoreAsync();
        var store = context.Store;
        var deliveredEnvelope = DurableInputStoreConformanceData.Envelope(messageId: "delivered");
        var deadEnvelope = DurableInputStoreConformanceData.Envelope(messageId: "dead-lettered");
        await store.EnqueueAsync(deliveredEnvelope);
        await store.EnqueueAsync(deadEnvelope);
        var leases = await store.LeaseAsync(DurableInputStoreConformanceData.Request(maxCount: 2));
        var deliveredLease = leases.Single(lease => lease.Envelope.Key == deliveredEnvelope.Key);
        var deadLease = leases.Single(lease => lease.Envelope.Key == deadEnvelope.Key);

        var delivered = await store.MarkDeliveredAsync(new DurableInputLeaseTransition(
            deliveredEnvelope.Key,
            deliveredLease.LeaseToken,
            DurableInputStoreConformanceData.Now.AddSeconds(1)));
        var deadLettered = await store.DeadLetterAsync(new DurableInputDeadLetter(
            deadEnvelope.Key,
            deadLease.LeaseToken,
            DurableInputStoreConformanceData.Now.AddSeconds(1),
            DurableInputStoreConformanceData.Failure(DurableInputFailureKind.InvalidEnvelope)));
        var deliveredRepeated = await store.MarkDeliveredAsync(new DurableInputLeaseTransition(
            deliveredEnvelope.Key,
            deliveredLease.LeaseToken,
            DurableInputStoreConformanceData.Now.AddSeconds(2)));
        var deadRepeated = await store.ReleaseAsync(new DurableInputRelease(
            deadEnvelope.Key,
            deadLease.LeaseToken,
            DurableInputStoreConformanceData.Now.AddSeconds(2),
            DurableInputStoreConformanceData.Now.AddMinutes(1),
            DurableInputStoreConformanceData.Failure()));
        var terminalLeases = await store.LeaseAsync(DurableInputStoreConformanceData.Request(
            now: DurableInputStoreConformanceData.Now.AddYears(1),
            leaseUntil: DurableInputStoreConformanceData.Now.AddYears(1).AddMinutes(1),
            maxCount: 2));
        var deliveredDuplicate = await store.EnqueueAsync(deliveredEnvelope);
        var deliveredConflict = await store.EnqueueAsync(
            DurableInputStoreConformanceData.Envelope(messageId: "delivered", value: "changed"));
        var deadDuplicate = await store.EnqueueAsync(deadEnvelope);
        var deadConflict = await store.EnqueueAsync(
            DurableInputStoreConformanceData.Envelope(messageId: "dead-lettered", value: "changed"));

        delivered.Status.ShouldBe(DurableInputTransitionStatus.Applied);
        deadLettered.Status.ShouldBe(DurableInputTransitionStatus.Applied);
        deliveredRepeated.Status.ShouldBe(DurableInputTransitionStatus.InvalidState);
        deadRepeated.Status.ShouldBe(DurableInputTransitionStatus.InvalidState);
        terminalLeases.ShouldBeEmpty();
        deliveredDuplicate.Status.ShouldBe(DurableInputEnqueueStatus.AlreadyExists);
        deliveredConflict.Status.ShouldBe(DurableInputEnqueueStatus.Conflict);
        deadDuplicate.Status.ShouldBe(DurableInputEnqueueStatus.AlreadyExists);
        deadConflict.Status.ShouldBe(DurableInputEnqueueStatus.Conflict);
    }

    [Fact]
    public async Task Precancelled_operations_do_not_mutate_enqueue_lease_or_transition_state()
    {
        await using var context = await CreateStoreAsync();
        var store = context.Store;
        var envelope = DurableInputStoreConformanceData.Envelope();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(
            () => store.EnqueueAsync(envelope, cancellation.Token).AsTask());
        var enqueue = await store.EnqueueAsync(envelope);
        await Should.ThrowAsync<OperationCanceledException>(
            () => store.LeaseAsync(
                DurableInputStoreConformanceData.Request(),
                cancellation.Token).AsTask());
        var firstLease = (await store.LeaseAsync(DurableInputStoreConformanceData.Request())).Single();
        var release = new DurableInputRelease(
            envelope.Key,
            firstLease.LeaseToken,
            DurableInputStoreConformanceData.Now.AddSeconds(1),
            DurableInputStoreConformanceData.Now.AddMinutes(1),
            DurableInputStoreConformanceData.Failure());
        await Should.ThrowAsync<OperationCanceledException>(
            () => store.ReleaseAsync(release, cancellation.Token).AsTask());
        var appliedRelease = await store.ReleaseAsync(release);
        var secondLease = (await store.LeaseAsync(DurableInputStoreConformanceData.Request(
            now: release.NextAttemptAt,
            leaseUntil: release.NextAttemptAt.AddSeconds(30)))).Single();
        var deadLetter = new DurableInputDeadLetter(
            envelope.Key,
            secondLease.LeaseToken,
            release.NextAttemptAt.AddSeconds(1),
            DurableInputStoreConformanceData.Failure(DurableInputFailureKind.InvalidEnvelope));
        await Should.ThrowAsync<OperationCanceledException>(
            () => store.DeadLetterAsync(deadLetter, cancellation.Token).AsTask());
        var delivery = new DurableInputLeaseTransition(
            envelope.Key,
            secondLease.LeaseToken,
            release.NextAttemptAt.AddSeconds(1));
        await Should.ThrowAsync<OperationCanceledException>(
            () => store.MarkDeliveredAsync(delivery, cancellation.Token).AsTask());
        var delivered = await store.MarkDeliveredAsync(delivery);

        enqueue.Status.ShouldBe(DurableInputEnqueueStatus.Enqueued);
        firstLease.Attempt.ShouldBe(1);
        appliedRelease.Status.ShouldBe(DurableInputTransitionStatus.Applied);
        secondLease.Attempt.ShouldBe(2);
        delivered.Status.ShouldBe(DurableInputTransitionStatus.Applied);
    }
}
