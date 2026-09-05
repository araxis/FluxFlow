using Shouldly;
using Xunit;

namespace FluxFlow.Engine.DurableInput.Tests;

/// <summary>
/// Provider-neutral behavioral contract for optional durable-input status stores.
/// Concrete provider projects inherit these tests against fresh real stores.
/// </summary>
public abstract class DurableInputStatusStoreConformanceTests
{
    private static readonly DateTimeOffset ObservedAt =
        new(2026, 8, 1, 14, 0, 0, TimeSpan.FromHours(2));

    protected abstract ValueTask<DurableInputStatusStoreTestContext> CreateStoreAsync();

    [Fact]
    public async Task Empty_store_returns_the_exact_zero_snapshot()
    {
        await using var context = await CreateStoreAsync();

        var snapshot = await context.StatusStore.GetStatusAsync(new(ObservedAt));

        AssertSnapshot(snapshot, 0, 0, 0, 0, 0, 0, null, null);
    }

    [Fact]
    public async Task Mixed_states_report_exact_counts_earliest_ready_and_next_active_expiry()
    {
        await using var context = await CreateStoreAsync();
        await AddDeliveredAsync(context, "status-delivered");
        await AddDeadLetteredAsync(context, "status-dead");
        await AddLeasedAsync(
            context,
            "status-active",
            ObservedAt.AddMinutes(-70),
            ObservedAt.AddMinutes(-60),
            ObservedAt.AddMinutes(20));
        await AddLeasedAsync(
            context,
            "status-expired",
            ObservedAt.AddMinutes(-50),
            ObservedAt.AddMinutes(-40),
            ObservedAt.AddMinutes(-10));
        await EnqueueAsync(context, "status-future", ObservedAt.AddMinutes(10));
        await EnqueueAsync(context, "status-ready", ObservedAt.AddMinutes(-90));

        var snapshot = await context.StatusStore.GetStatusAsync(new(ObservedAt));

        AssertSnapshot(
            snapshot,
            pending: 2,
            readyPending: 1,
            leased: 2,
            expiredLease: 1,
            delivered: 1,
            deadLettered: 1,
            oldestReadyAt: ObservedAt.AddMinutes(-90),
            nextLeaseExpiry: ObservedAt.AddMinutes(20));
    }

    [Fact]
    public async Task Exact_due_and_expiry_boundaries_are_ready_while_future_values_are_not()
    {
        await using var context = await CreateStoreAsync();
        await AddLeasedAsync(
            context,
            "boundary-active",
            ObservedAt.AddMinutes(-30),
            ObservedAt.AddMinutes(-20),
            ObservedAt.AddTicks(1));
        await AddLeasedAsync(
            context,
            "boundary-expired",
            ObservedAt.AddMinutes(-15),
            ObservedAt.AddMinutes(-10),
            ObservedAt);
        await EnqueueAsync(context, "boundary-future", ObservedAt.AddTicks(1));
        await EnqueueAsync(context, "boundary-ready", ObservedAt);

        var snapshot = await context.StatusStore.GetStatusAsync(new(ObservedAt));

        AssertSnapshot(
            snapshot,
            pending: 2,
            readyPending: 1,
            leased: 2,
            expiredLease: 1,
            delivered: 0,
            deadLettered: 0,
            oldestReadyAt: ObservedAt,
            nextLeaseExpiry: ObservedAt.AddTicks(1));
    }

    [Fact]
    public async Task Status_does_not_mutate_consume_or_cache_store_state()
    {
        await using var context = await CreateStoreAsync();
        var envelope = await EnqueueAsync(
            context,
            "status-transition",
            ObservedAt.AddMinutes(-1));

        var first = await context.StatusStore.GetStatusAsync(new(ObservedAt));
        var repeated = await context.StatusStore.GetStatusAsync(new(ObservedAt));
        var lease = (await context.Store.LeaseAsync(new(
            "status-owner",
            ObservedAt,
            ObservedAt.AddMinutes(10),
            maxCount: 1))).ShouldHaveSingleItem();
        var afterLease = await context.StatusStore.GetStatusAsync(new(ObservedAt));
        var transition = await context.Store.MarkDeliveredAsync(new(
            envelope.Key,
            lease.LeaseToken,
            ObservedAt.AddMinutes(1)));
        var afterDelivery = await context.StatusStore.GetStatusAsync(new(ObservedAt));

        repeated.ShouldBe(first);
        AssertSnapshot(first, 1, 1, 0, 0, 0, 0, ObservedAt.AddMinutes(-1), null);
        AssertSnapshot(
            afterLease,
            0,
            0,
            1,
            0,
            0,
            0,
            null,
            ObservedAt.AddMinutes(10));
        transition.Status.ShouldBe(DurableInputTransitionStatus.Applied);
        AssertSnapshot(afterDelivery, 0, 0, 0, 0, 1, 0, null, null);
    }

    [Fact]
    public async Task Precancelled_status_preserves_the_committed_state()
    {
        await using var context = await CreateStoreAsync();
        await EnqueueAsync(context, "status-cancelled", ObservedAt.AddMinutes(-2));
        using var source = new CancellationTokenSource();
        source.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(() =>
            context.StatusStore.GetStatusAsync(new(ObservedAt), source.Token).AsTask());
        var snapshot = await context.StatusStore.GetStatusAsync(new(ObservedAt));

        AssertSnapshot(snapshot, 1, 1, 0, 0, 0, 0, ObservedAt.AddMinutes(-2), null);
    }

    private static async ValueTask<DurableInputEnvelope> EnqueueAsync(
        DurableInputStatusStoreTestContext context,
        string messageId,
        DateTimeOffset enqueuedAt)
    {
        var envelope = DurableInputStoreConformanceData.Envelope(
            messageId,
            enqueuedAt: enqueuedAt,
            timestamp: enqueuedAt.AddMinutes(-1),
            traceId: $"trace-{messageId}");
        var result = await context.Store.EnqueueAsync(envelope);
        result.Status.ShouldBe(DurableInputEnqueueStatus.Enqueued);
        return envelope;
    }

    private static async ValueTask<DurableInputLease> AddLeasedAsync(
        DurableInputStatusStoreTestContext context,
        string messageId,
        DateTimeOffset enqueuedAt,
        DateTimeOffset leasedAt,
        DateTimeOffset leaseUntil)
    {
        await EnqueueAsync(context, messageId, enqueuedAt);
        var leases = await context.Store.LeaseAsync(new(
            $"owner-{messageId}",
            leasedAt,
            leaseUntil,
            maxCount: 1));
        return leases.ShouldHaveSingleItem();
    }

    private static async ValueTask AddDeliveredAsync(
        DurableInputStatusStoreTestContext context,
        string messageId)
    {
        var lease = await AddLeasedAsync(
            context,
            messageId,
            ObservedAt.AddMinutes(-120),
            ObservedAt.AddMinutes(-110),
            ObservedAt.AddMinutes(-100));
        var result = await context.Store.MarkDeliveredAsync(new(
            lease.Envelope.Key,
            lease.LeaseToken,
            ObservedAt.AddMinutes(-105)));
        result.Status.ShouldBe(DurableInputTransitionStatus.Applied);
    }

    private static async ValueTask AddDeadLetteredAsync(
        DurableInputStatusStoreTestContext context,
        string messageId)
    {
        var lease = await AddLeasedAsync(
            context,
            messageId,
            ObservedAt.AddMinutes(-100),
            ObservedAt.AddMinutes(-90),
            ObservedAt.AddMinutes(-80));
        var result = await context.Store.DeadLetterAsync(new(
            lease.Envelope.Key,
            lease.LeaseToken,
            ObservedAt.AddMinutes(-85),
            DurableInputStoreConformanceData.Failure()));
        result.Status.ShouldBe(DurableInputTransitionStatus.Applied);
    }

    private static void AssertSnapshot(
        DurableInputStatusSnapshot snapshot,
        long pending,
        long readyPending,
        long leased,
        long expiredLease,
        long delivered,
        long deadLettered,
        DateTimeOffset? oldestReadyAt,
        DateTimeOffset? nextLeaseExpiry)
    {
        snapshot.ObservedAt.ShouldBe(ObservedAt);
        snapshot.PendingCount.ShouldBe(pending);
        snapshot.ReadyPendingCount.ShouldBe(readyPending);
        snapshot.LeasedCount.ShouldBe(leased);
        snapshot.ExpiredLeaseCount.ShouldBe(expiredLease);
        snapshot.DeliveredCount.ShouldBe(delivered);
        snapshot.DeadLetteredCount.ShouldBe(deadLettered);
        snapshot.OldestReadyAt.ShouldBe(oldestReadyAt);
        snapshot.NextLeaseExpiry.ShouldBe(nextLeaseExpiry);
        snapshot.TotalCount.ShouldBe(pending + leased + delivered + deadLettered);
    }
}
