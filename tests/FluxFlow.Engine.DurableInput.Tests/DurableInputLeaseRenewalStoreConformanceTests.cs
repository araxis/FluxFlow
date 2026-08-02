using Shouldly;
using Xunit;

namespace FluxFlow.Engine.DurableInput.Tests;

public abstract class DurableInputLeaseRenewalStoreConformanceTests
{
    protected abstract ValueTask<DurableInputLeaseRenewalStoreTestContext> CreateStoreAsync();

    [Fact]
    public async Task Current_exact_token_renews_expiry_without_changing_envelope_or_attempt()
    {
        await using var context = await CreateStoreAsync();
        var envelope = DurableInputStoreConformanceData.Envelope(messageId: "renew-current");
        await context.Store.EnqueueAsync(envelope);
        var lease = (await context.Store.LeaseAsync(DurableInputStoreConformanceData.Request(
            leaseUntil: DurableInputStoreConformanceData.Now.AddSeconds(30)))).Single();
        var extendedUntil = DurableInputStoreConformanceData.Now.AddMinutes(2);

        var result = await context.RenewalStore.RenewLeaseAsync(new DurableInputLeaseRenewal(
            envelope.Key,
            lease.LeaseToken,
            DurableInputStoreConformanceData.Now.AddSeconds(10),
            extendedUntil));
        var beforeExtendedExpiry = await context.Store.LeaseAsync(
            DurableInputStoreConformanceData.Request(
                ownerId: "other",
                now: extendedUntil.AddTicks(-1),
                leaseUntil: extendedUntil.AddMinutes(1)));
        var recovered = (await context.Store.LeaseAsync(
            DurableInputStoreConformanceData.Request(
                ownerId: "other",
                now: extendedUntil,
                leaseUntil: extendedUntil.AddMinutes(1)))).Single();

        result.Status.ShouldBe(DurableInputTransitionStatus.Applied);
        beforeExtendedExpiry.ShouldBeEmpty();
        recovered.Envelope.HasSameContent(envelope).ShouldBeTrue();
        recovered.Envelope.EnqueuedAt.ShouldBe(envelope.EnqueuedAt);
        recovered.Attempt.ShouldBe(2);
        recovered.OwnerId.ShouldBe("other");
        recovered.LeaseToken.ShouldNotBe(lease.LeaseToken);
    }

    [Fact]
    public async Task Renewal_sets_the_exact_requested_expiry_even_when_it_is_earlier_than_the_previous_expiry()
    {
        await using var context = await CreateStoreAsync();
        var envelope = DurableInputStoreConformanceData.Envelope(messageId: "renew-exact-expiry");
        await context.Store.EnqueueAsync(envelope);
        var originalUntil = DurableInputStoreConformanceData.Now.AddMinutes(2);
        var lease = (await context.Store.LeaseAsync(DurableInputStoreConformanceData.Request(
            leaseUntil: originalUntil))).Single();
        var requestedUntil = DurableInputStoreConformanceData.Now.AddSeconds(30);

        var renewal = await context.RenewalStore.RenewLeaseAsync(new DurableInputLeaseRenewal(
            envelope.Key,
            lease.LeaseToken,
            DurableInputStoreConformanceData.Now.AddSeconds(1),
            requestedUntil));
        var beforeRequestedExpiry = await context.Store.LeaseAsync(
            DurableInputStoreConformanceData.Request(
                ownerId: "other",
                now: requestedUntil.AddTicks(-1),
                leaseUntil: originalUntil.AddMinutes(1)));
        var atRequestedExpiry = (await context.Store.LeaseAsync(
            DurableInputStoreConformanceData.Request(
                ownerId: "other",
                now: requestedUntil,
                leaseUntil: originalUntil.AddMinutes(1)))).Single();

        renewal.Status.ShouldBe(DurableInputTransitionStatus.Applied);
        beforeRequestedExpiry.ShouldBeEmpty();
        atRequestedExpiry.Attempt.ShouldBe(2);
        atRequestedExpiry.Envelope.HasSameContent(envelope).ShouldBeTrue();
        atRequestedExpiry.Envelope.EnqueuedAt.ShouldBe(envelope.EnqueuedAt);
    }

    [Fact]
    public async Task Wrong_token_cannot_renew_and_current_token_remains_settleable()
    {
        await using var context = await CreateStoreAsync();
        var envelope = DurableInputStoreConformanceData.Envelope(messageId: "renew-wrong-token");
        await context.Store.EnqueueAsync(envelope);
        var lease = (await context.Store.LeaseAsync(DurableInputStoreConformanceData.Request())).Single();

        var renewal = await context.RenewalStore.RenewLeaseAsync(new DurableInputLeaseRenewal(
            envelope.Key,
            Guid.NewGuid(),
            DurableInputStoreConformanceData.Now.AddSeconds(1),
            DurableInputStoreConformanceData.Now.AddMinutes(1)));
        var delivered = await context.Store.MarkDeliveredAsync(new DurableInputLeaseTransition(
            envelope.Key,
            lease.LeaseToken,
            DurableInputStoreConformanceData.Now.AddSeconds(2)));

        renewal.Status.ShouldBe(DurableInputTransitionStatus.LeaseLost);
        delivered.Status.ShouldBe(DurableInputTransitionStatus.Applied);
    }

    [Fact]
    public async Task Successful_renewal_preserves_the_same_token_for_later_settlement()
    {
        await using var context = await CreateStoreAsync();
        var envelope = DurableInputStoreConformanceData.Envelope(messageId: "renew-then-settle");
        await context.Store.EnqueueAsync(envelope);
        var lease = (await context.Store.LeaseAsync(DurableInputStoreConformanceData.Request())).Single();
        var renewedAt = DurableInputStoreConformanceData.Now.AddSeconds(1);

        var renewal = await context.RenewalStore.RenewLeaseAsync(
            Renewal(envelope.Key, lease.LeaseToken, renewedAt));
        var delivered = await context.Store.MarkDeliveredAsync(new DurableInputLeaseTransition(
            envelope.Key,
            lease.LeaseToken,
            renewedAt.AddSeconds(1)));
        var future = await context.Store.LeaseAsync(DurableInputStoreConformanceData.Request(
            now: renewedAt.AddYears(1),
            leaseUntil: renewedAt.AddYears(1).AddMinutes(1)));

        renewal.Status.ShouldBe(DurableInputTransitionStatus.Applied);
        delivered.Status.ShouldBe(DurableInputTransitionStatus.Applied);
        future.ShouldBeEmpty();
    }

    [Fact]
    public async Task Expired_lease_cannot_be_revived_and_recovery_gets_a_new_attempt_and_token()
    {
        await using var context = await CreateStoreAsync();
        var envelope = DurableInputStoreConformanceData.Envelope(messageId: "renew-expired");
        await context.Store.EnqueueAsync(envelope);
        var lease = (await context.Store.LeaseAsync(DurableInputStoreConformanceData.Request(
            leaseUntil: DurableInputStoreConformanceData.Now.AddSeconds(5)))).Single();

        var renewal = await context.RenewalStore.RenewLeaseAsync(new DurableInputLeaseRenewal(
            envelope.Key,
            lease.LeaseToken,
            lease.LeaseUntil,
            lease.LeaseUntil.AddMinutes(1)));
        var recovered = (await context.Store.LeaseAsync(
            DurableInputStoreConformanceData.Request(
                ownerId: "recovery",
                now: lease.LeaseUntil,
                leaseUntil: lease.LeaseUntil.AddMinutes(1)))).Single();

        renewal.Status.ShouldBe(DurableInputTransitionStatus.LeaseLost);
        recovered.Attempt.ShouldBe(2);
        recovered.LeaseToken.ShouldNotBe(lease.LeaseToken);
        recovered.Envelope.HasSameContent(envelope).ShouldBeTrue();
        recovered.Envelope.EnqueuedAt.ShouldBe(envelope.EnqueuedAt);
    }

    [Fact]
    public async Task Missing_pending_delivered_and_dead_lettered_records_are_not_renewed()
    {
        await using var context = await CreateStoreAsync();
        var pending = DurableInputStoreConformanceData.Envelope(
            messageId: "renew-pending",
            enqueuedAt: DurableInputStoreConformanceData.Now.AddMinutes(10));
        var delivered = DurableInputStoreConformanceData.Envelope(messageId: "renew-delivered");
        var dead = DurableInputStoreConformanceData.Envelope(messageId: "renew-dead");
        await context.Store.EnqueueAsync(pending);
        await context.Store.EnqueueAsync(delivered);
        await context.Store.EnqueueAsync(dead);
        var leases = await context.Store.LeaseAsync(DurableInputStoreConformanceData.Request(
            maxCount: 2));
        var deliveredLease = leases.Single(item => item.Envelope.Key == delivered.Key);
        var deadLease = leases.Single(item => item.Envelope.Key == dead.Key);
        (await context.Store.MarkDeliveredAsync(new DurableInputLeaseTransition(
            delivered.Key,
            deliveredLease.LeaseToken,
            DurableInputStoreConformanceData.Now.AddSeconds(1)))).IsApplied.ShouldBeTrue();
        (await context.Store.DeadLetterAsync(new DurableInputDeadLetter(
            dead.Key,
            deadLease.LeaseToken,
            DurableInputStoreConformanceData.Now.AddSeconds(1),
            DurableInputStoreConformanceData.Failure()))).IsApplied.ShouldBeTrue();
        var renewalTime = DurableInputStoreConformanceData.Now.AddSeconds(2);

        var missingResult = await context.RenewalStore.RenewLeaseAsync(Renewal(
            DurableInputStoreConformanceData.Envelope(messageId: "renew-missing").Key,
            Guid.NewGuid(),
            renewalTime));
        var pendingResult = await context.RenewalStore.RenewLeaseAsync(Renewal(
            pending.Key,
            Guid.NewGuid(),
            renewalTime));
        var deliveredResult = await context.RenewalStore.RenewLeaseAsync(Renewal(
            delivered.Key,
            deliveredLease.LeaseToken,
            renewalTime));
        var deadResult = await context.RenewalStore.RenewLeaseAsync(Renewal(
            dead.Key,
            deadLease.LeaseToken,
            renewalTime));

        missingResult.Status.ShouldBe(DurableInputTransitionStatus.NotFound);
        pendingResult.Status.ShouldBe(DurableInputTransitionStatus.InvalidState);
        deliveredResult.Status.ShouldBe(DurableInputTransitionStatus.InvalidState);
        deadResult.Status.ShouldBe(DurableInputTransitionStatus.InvalidState);
    }

    [Fact]
    public async Task Pre_cancelled_renewal_does_not_consume_the_current_lease()
    {
        await using var context = await CreateStoreAsync();
        var envelope = DurableInputStoreConformanceData.Envelope(messageId: "renew-cancelled");
        await context.Store.EnqueueAsync(envelope);
        var lease = (await context.Store.LeaseAsync(DurableInputStoreConformanceData.Request())).Single();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(async () =>
            await context.RenewalStore.RenewLeaseAsync(
                Renewal(
                    envelope.Key,
                    lease.LeaseToken,
                    DurableInputStoreConformanceData.Now.AddSeconds(1)),
                cancellation.Token));
        var delivered = await context.Store.MarkDeliveredAsync(new DurableInputLeaseTransition(
            envelope.Key,
            lease.LeaseToken,
            DurableInputStoreConformanceData.Now.AddSeconds(2)));

        delivered.Status.ShouldBe(DurableInputTransitionStatus.Applied);
    }

    [Fact]
    public async Task Concurrent_renewal_and_delivery_never_revive_a_settled_record()
    {
        await using var context = await CreateStoreAsync();
        var envelope = DurableInputStoreConformanceData.Envelope(messageId: "renew-race");
        await context.Store.EnqueueAsync(envelope);
        var lease = (await context.Store.LeaseAsync(DurableInputStoreConformanceData.Request())).Single();
        var occurredAt = DurableInputStoreConformanceData.Now.AddSeconds(1);

        var renewalTask = context.RenewalStore.RenewLeaseAsync(
            Renewal(envelope.Key, lease.LeaseToken, occurredAt)).AsTask();
        var deliveredTask = context.Store.MarkDeliveredAsync(new DurableInputLeaseTransition(
            envelope.Key,
            lease.LeaseToken,
            occurredAt)).AsTask();
        await Task.WhenAll(renewalTask, deliveredTask);
        var renewalResult = await renewalTask;
        var deliveredResult = await deliveredTask;
        var farFuture = await context.Store.LeaseAsync(DurableInputStoreConformanceData.Request(
            now: occurredAt.AddYears(1),
            leaseUntil: occurredAt.AddYears(1).AddMinutes(1)));

        deliveredResult.Status.ShouldBe(DurableInputTransitionStatus.Applied);
        renewalResult.Status.ShouldBeOneOf(
            DurableInputTransitionStatus.Applied,
            DurableInputTransitionStatus.InvalidState);
        farFuture.ShouldBeEmpty();
    }

    [Fact]
    public async Task Concurrent_renewal_and_release_never_overwrite_the_retry_schedule()
    {
        await using var context = await CreateStoreAsync();
        var envelope = DurableInputStoreConformanceData.Envelope(messageId: "renew-release-race");
        await context.Store.EnqueueAsync(envelope);
        var lease = (await context.Store.LeaseAsync(DurableInputStoreConformanceData.Request())).Single();
        var occurredAt = DurableInputStoreConformanceData.Now.AddSeconds(1);
        var nextAttemptAt = occurredAt.AddMinutes(5);
        var failure = DurableInputStoreConformanceData.Failure(
            description: "retry after terminal operation");

        var renewalTask = context.RenewalStore.RenewLeaseAsync(
            Renewal(envelope.Key, lease.LeaseToken, occurredAt)).AsTask();
        var releaseTask = context.Store.ReleaseAsync(new DurableInputRelease(
            envelope.Key,
            lease.LeaseToken,
            occurredAt,
            nextAttemptAt,
            failure)).AsTask();
        await Task.WhenAll(renewalTask, releaseTask);
        var renewalResult = await renewalTask;
        var releaseResult = await releaseTask;
        var beforeDue = await context.Store.LeaseAsync(DurableInputStoreConformanceData.Request(
            now: nextAttemptAt.AddTicks(-1),
            leaseUntil: nextAttemptAt.AddMinutes(1)));
        var atDue = (await context.Store.LeaseAsync(DurableInputStoreConformanceData.Request(
            now: nextAttemptAt,
            leaseUntil: nextAttemptAt.AddMinutes(1)))).Single();

        releaseResult.Status.ShouldBe(DurableInputTransitionStatus.Applied);
        renewalResult.Status.ShouldBeOneOf(
            DurableInputTransitionStatus.Applied,
            DurableInputTransitionStatus.InvalidState);
        beforeDue.ShouldBeEmpty();
        atDue.Attempt.ShouldBe(2);
        atDue.Envelope.HasSameContent(envelope).ShouldBeTrue();
    }

    [Fact]
    public async Task Concurrent_renewal_and_dead_letter_never_revive_the_terminal_record()
    {
        await using var context = await CreateStoreAsync();
        var envelope = DurableInputStoreConformanceData.Envelope(messageId: "renew-dead-race");
        await context.Store.EnqueueAsync(envelope);
        var lease = (await context.Store.LeaseAsync(DurableInputStoreConformanceData.Request())).Single();
        var occurredAt = DurableInputStoreConformanceData.Now.AddSeconds(1);
        var failure = DurableInputStoreConformanceData.Failure(
            DurableInputFailureKind.WorkflowCompletionFailed,
            "terminal operation failed");

        var renewalTask = context.RenewalStore.RenewLeaseAsync(
            Renewal(envelope.Key, lease.LeaseToken, occurredAt)).AsTask();
        var deadLetterTask = context.Store.DeadLetterAsync(new DurableInputDeadLetter(
            envelope.Key,
            lease.LeaseToken,
            occurredAt,
            failure)).AsTask();
        await Task.WhenAll(renewalTask, deadLetterTask);
        var renewalResult = await renewalTask;
        var deadLetterResult = await deadLetterTask;
        var farFuture = await context.Store.LeaseAsync(DurableInputStoreConformanceData.Request(
            now: occurredAt.AddYears(1),
            leaseUntil: occurredAt.AddYears(1).AddMinutes(1)));

        deadLetterResult.Status.ShouldBe(DurableInputTransitionStatus.Applied);
        renewalResult.Status.ShouldBeOneOf(
            DurableInputTransitionStatus.Applied,
            DurableInputTransitionStatus.InvalidState);
        farFuture.ShouldBeEmpty();
    }

    private static DurableInputLeaseRenewal Renewal(
        DurableInputKey key,
        Guid token,
        DateTimeOffset renewedAt)
        => new(key, token, renewedAt, renewedAt.AddMinutes(1));
}
