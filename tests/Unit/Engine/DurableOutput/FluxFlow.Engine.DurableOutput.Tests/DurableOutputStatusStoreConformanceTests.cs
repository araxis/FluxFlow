using Shouldly;
using Xunit;

namespace FluxFlow.Engine.DurableOutput.Tests;

/// <summary>
/// Provider-neutral behavioral contract for optional durable-output status stores.
/// Concrete provider projects inherit these tests against fresh real stores.
/// </summary>
public abstract class DurableOutputStatusStoreConformanceTests
{
    private static readonly DateTimeOffset ObservedAt =
        new(2026, 8, 1, 14, 0, 0, TimeSpan.FromHours(-3));

    protected abstract ValueTask<DurableOutputStatusStoreTestContext> CreateStoreAsync();

    [Fact]
    public async Task Empty_store_returns_the_exact_zero_snapshot()
    {
        await using var context = await CreateStoreAsync();

        var snapshot = await context.StatusStore.GetStatusAsync(new(ObservedAt));

        AssertSnapshot(snapshot, 0, 0, 0, 0, 0, 0, 0, 0, 0, null, null);
    }

    [Fact]
    public async Task Mixed_states_include_unmaterialized_and_report_exact_counts_and_times()
    {
        await using var context = await CreateStoreAsync();
        await AddCompletedAsync(context, "status-completed");
        await AddDeadLetteredAsync(context, "status-dead");
        await AddLeasedAsync(
            context,
            "status-active",
            ObservedAt.AddMinutes(-140),
            ObservedAt.AddMinutes(-130),
            ObservedAt.AddMinutes(20));
        await AddLeasedAsync(
            context,
            "status-expired",
            ObservedAt.AddMinutes(-120),
            ObservedAt.AddMinutes(-110),
            ObservedAt.AddMinutes(-10));
        await AddPendingAsync(
            context,
            "status-pending-future",
            ObservedAt.AddMinutes(-100),
            ObservedAt.AddMinutes(-90),
            ObservedAt.AddMinutes(10));
        await AddPendingAsync(
            context,
            "status-pending-ready",
            ObservedAt.AddMinutes(-70),
            ObservedAt.AddMinutes(-60),
            ObservedAt.AddMinutes(-40));
        await EnqueueAsync(context, "status-unmaterialized-ready", ObservedAt.AddMinutes(-90));
        await EnqueueAsync(context, "status-unmaterialized-future", ObservedAt.AddMinutes(30));

        var snapshot = await context.StatusStore.GetStatusAsync(new(ObservedAt));

        AssertSnapshot(
            snapshot,
            captured: 8,
            unmaterialized: 2,
            readyUnmaterialized: 1,
            pending: 2,
            readyPending: 1,
            leased: 2,
            expiredLease: 1,
            completed: 1,
            deadLettered: 1,
            oldestReadyAt: ObservedAt.AddMinutes(-90),
            nextLeaseExpiry: ObservedAt.AddMinutes(20));
    }

    [Fact]
    public async Task Exact_capture_due_and_expiry_boundaries_are_ready_while_future_values_are_not()
    {
        await using var context = await CreateStoreAsync();
        await AddLeasedAsync(
            context,
            "boundary-active",
            ObservedAt.AddMinutes(-10),
            ObservedAt.AddMinutes(-9),
            ObservedAt.AddTicks(1));
        await AddLeasedAsync(
            context,
            "boundary-expired",
            ObservedAt.AddMinutes(-8),
            ObservedAt.AddMinutes(-7),
            ObservedAt);
        await AddPendingAsync(
            context,
            "boundary-pending-future",
            ObservedAt.AddMinutes(-6),
            ObservedAt.AddMinutes(-5),
            ObservedAt.AddTicks(1));
        await AddPendingAsync(
            context,
            "boundary-pending-ready",
            ObservedAt.AddMinutes(-3),
            ObservedAt.AddMinutes(-2),
            ObservedAt);
        await EnqueueAsync(context, "boundary-unmaterialized-ready", ObservedAt);
        await EnqueueAsync(context, "boundary-unmaterialized-future", ObservedAt.AddTicks(1));

        var snapshot = await context.StatusStore.GetStatusAsync(new(ObservedAt));

        AssertSnapshot(
            snapshot,
            captured: 6,
            unmaterialized: 2,
            readyUnmaterialized: 1,
            pending: 2,
            readyPending: 1,
            leased: 2,
            expiredLease: 1,
            completed: 0,
            deadLettered: 0,
            oldestReadyAt: ObservedAt,
            nextLeaseExpiry: ObservedAt.AddTicks(1));
    }

    [Fact]
    public async Task Status_does_not_materialize_consume_or_cache_delivery_state()
    {
        await using var context = await CreateStoreAsync();
        var envelope = await EnqueueAsync(
            context,
            "status-transition",
            ObservedAt.AddMinutes(-1));

        var first = await context.StatusStore.GetStatusAsync(new(ObservedAt));
        var repeated = await context.StatusStore.GetStatusAsync(new(ObservedAt));
        var lease = (await context.DeliveryStore.TryLeaseAsync(new(
            "status-owner",
            ObservedAt,
            ObservedAt.AddMinutes(10)))).ShouldNotBeNull();
        var afterLease = await context.StatusStore.GetStatusAsync(new(ObservedAt));
        var completion = await context.DeliveryStore.CompleteAsync(new(
            envelope.Key,
            lease.LeaseToken,
            ObservedAt.AddMinutes(1)));
        var afterCompletion = await context.StatusStore.GetStatusAsync(new(ObservedAt));

        repeated.ShouldBe(first);
        AssertSnapshot(first, 1, 1, 1, 0, 0, 0, 0, 0, 0, ObservedAt.AddMinutes(-1), null);
        AssertSnapshot(
            afterLease,
            1,
            0,
            0,
            0,
            0,
            1,
            0,
            0,
            0,
            null,
            ObservedAt.AddMinutes(10));
        completion.Status.ShouldBe(DurableOutputDeliveryTransitionStatus.Applied);
        AssertSnapshot(afterCompletion, 1, 0, 0, 0, 0, 0, 0, 1, 0, null, null);
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

        AssertSnapshot(snapshot, 1, 1, 1, 0, 0, 0, 0, 0, 0, ObservedAt.AddMinutes(-2), null);
    }

    private static async ValueTask<DurableOutputEnvelope> EnqueueAsync(
        DurableOutputStatusStoreTestContext context,
        string messageId,
        DateTimeOffset capturedAt)
    {
        var envelope = DurableOutputStoreConformanceData.Envelope(
            messageId,
            capturedAt: capturedAt,
            timestamp: capturedAt.AddMinutes(-1),
            traceId: $"trace-{messageId}");
        var result = await context.CaptureStore.EnqueueAsync(envelope);
        result.Status.ShouldBe(DurableOutputEnqueueStatus.Enqueued);
        return envelope;
    }

    private static async ValueTask<DurableOutputDeliveryLease> AddLeasedAsync(
        DurableOutputStatusStoreTestContext context,
        string messageId,
        DateTimeOffset capturedAt,
        DateTimeOffset leasedAt,
        DateTimeOffset leaseUntil)
    {
        await EnqueueAsync(context, messageId, capturedAt);
        return (await context.DeliveryStore.TryLeaseAsync(new(
            $"owner-{messageId}",
            leasedAt,
            leaseUntil))).ShouldNotBeNull();
    }

    private static async ValueTask AddPendingAsync(
        DurableOutputStatusStoreTestContext context,
        string messageId,
        DateTimeOffset capturedAt,
        DateTimeOffset leasedAt,
        DateTimeOffset nextAttemptAt)
    {
        var leaseUntil = leasedAt.AddMinutes(2);
        var lease = await AddLeasedAsync(
            context,
            messageId,
            capturedAt,
            leasedAt,
            leaseUntil);
        var result = await context.DeliveryStore.RetryAsync(new(
            lease.Envelope.Key,
            lease.LeaseToken,
            leaseUntil.AddTicks(-1),
            nextAttemptAt));
        result.Status.ShouldBe(DurableOutputDeliveryTransitionStatus.Applied);
    }

    private static async ValueTask AddCompletedAsync(
        DurableOutputStatusStoreTestContext context,
        string messageId)
    {
        var lease = await AddLeasedAsync(
            context,
            messageId,
            ObservedAt.AddMinutes(-200),
            ObservedAt.AddMinutes(-190),
            ObservedAt.AddMinutes(-180));
        var result = await context.DeliveryStore.CompleteAsync(new(
            lease.Envelope.Key,
            lease.LeaseToken,
            ObservedAt.AddMinutes(-185)));
        result.Status.ShouldBe(DurableOutputDeliveryTransitionStatus.Applied);
    }

    private static async ValueTask AddDeadLetteredAsync(
        DurableOutputStatusStoreTestContext context,
        string messageId)
    {
        var lease = await AddLeasedAsync(
            context,
            messageId,
            ObservedAt.AddMinutes(-170),
            ObservedAt.AddMinutes(-160),
            ObservedAt.AddMinutes(-150));
        var result = await context.DeliveryStore.DeadLetterAsync(
            DurableOutputStoreConformanceData.DeadLetter(
                lease.Envelope.Key,
                lease.LeaseToken,
                ObservedAt.AddMinutes(-155)));
        result.Status.ShouldBe(DurableOutputDeliveryTransitionStatus.Applied);
    }

    private static void AssertSnapshot(
        DurableOutputStatusSnapshot snapshot,
        long captured,
        long unmaterialized,
        long readyUnmaterialized,
        long pending,
        long readyPending,
        long leased,
        long expiredLease,
        long completed,
        long deadLettered,
        DateTimeOffset? oldestReadyAt,
        DateTimeOffset? nextLeaseExpiry)
    {
        snapshot.ObservedAt.ShouldBe(ObservedAt);
        snapshot.CapturedCount.ShouldBe(captured);
        snapshot.UnmaterializedCount.ShouldBe(unmaterialized);
        snapshot.ReadyUnmaterializedCount.ShouldBe(readyUnmaterialized);
        snapshot.PendingCount.ShouldBe(pending);
        snapshot.ReadyPendingCount.ShouldBe(readyPending);
        snapshot.LeasedCount.ShouldBe(leased);
        snapshot.ExpiredLeaseCount.ShouldBe(expiredLease);
        snapshot.CompletedCount.ShouldBe(completed);
        snapshot.DeadLetteredCount.ShouldBe(deadLettered);
        snapshot.OldestReadyAt.ShouldBe(oldestReadyAt);
        snapshot.NextLeaseExpiry.ShouldBe(nextLeaseExpiry);
        snapshot.TrackedDeliveryCount.ShouldBe(pending + leased + completed + deadLettered);
        snapshot.ReadyCount.ShouldBe(readyUnmaterialized + readyPending + expiredLease);
    }
}
