using Shouldly;
using Xunit;

namespace FluxFlow.Engine.DurableOutput.Tests;

public sealed class DurableOutputStatusContractsTests
{
    private static readonly DateTimeOffset ObservedAt =
        new(2026, 8, 1, 12, 30, 0, TimeSpan.FromHours(-3));

    [Fact]
    public void Query_preserves_exact_observed_at_and_offset()
    {
        var query = new DurableOutputStatusQuery(ObservedAt);

        query.ObservedAt.ShouldBe(ObservedAt);
        query.ObservedAt.Offset.ShouldBe(TimeSpan.FromHours(-3));
    }

    [Fact]
    public void Snapshot_preserves_valid_mixed_values_and_computes_checked_totals()
    {
        var oldestReadyAt = ObservedAt.AddMinutes(-40);
        var nextLeaseExpiry = ObservedAt.AddMinutes(20);

        var snapshot = new DurableOutputStatusSnapshot(
            ObservedAt,
            capturedCount: 15,
            unmaterializedCount: 2,
            readyUnmaterializedCount: 1,
            pendingCount: 3,
            readyPendingCount: 2,
            leasedCount: 4,
            expiredLeaseCount: 1,
            completedCount: 5,
            deadLetteredCount: 1,
            oldestReadyAt,
            nextLeaseExpiry);

        snapshot.ObservedAt.ShouldBe(ObservedAt);
        snapshot.CapturedCount.ShouldBe(15);
        snapshot.UnmaterializedCount.ShouldBe(2);
        snapshot.ReadyUnmaterializedCount.ShouldBe(1);
        snapshot.PendingCount.ShouldBe(3);
        snapshot.ReadyPendingCount.ShouldBe(2);
        snapshot.LeasedCount.ShouldBe(4);
        snapshot.ExpiredLeaseCount.ShouldBe(1);
        snapshot.CompletedCount.ShouldBe(5);
        snapshot.DeadLetteredCount.ShouldBe(1);
        snapshot.OldestReadyAt.ShouldBe(oldestReadyAt);
        snapshot.NextLeaseExpiry.ShouldBe(nextLeaseExpiry);
        snapshot.TrackedDeliveryCount.ShouldBe(13);
        snapshot.ReadyCount.ShouldBe(4);
    }

    [Fact]
    public void Snapshot_accepts_each_valid_readiness_and_expiry_shape()
    {
        var snapshots = new[]
        {
            Snapshot(),
            Snapshot(captured: 1, unmaterialized: 1),
            Snapshot(
                captured: 1,
                unmaterialized: 1,
                readyUnmaterialized: 1,
                oldestReadyAt: ObservedAt),
            Snapshot(captured: 1, pending: 1),
            Snapshot(
                captured: 1,
                pending: 1,
                readyPending: 1,
                oldestReadyAt: ObservedAt),
            Snapshot(
                captured: 1,
                leased: 1,
                nextLeaseExpiry: ObservedAt.AddTicks(1)),
            Snapshot(
                captured: 1,
                leased: 1,
                expiredLease: 1,
                oldestReadyAt: ObservedAt),
            Snapshot(captured: 1, completed: 1),
            Snapshot(captured: 1, deadLettered: 1)
        };

        snapshots.Select(snapshot => snapshot.CapturedCount)
            .ShouldBe(new long[] { 0, 1, 1, 1, 1, 1, 1, 1, 1 });
        snapshots.Select(snapshot => snapshot.ReadyCount)
            .ShouldBe(new long[] { 0, 0, 1, 0, 1, 0, 1, 0, 0 });
        snapshots[5].NextLeaseExpiry.ShouldBe(ObservedAt.AddTicks(1));
    }

    [Theory]
    [InlineData("capturedCount")]
    [InlineData("unmaterializedCount")]
    [InlineData("readyUnmaterializedCount")]
    [InlineData("pendingCount")]
    [InlineData("readyPendingCount")]
    [InlineData("leasedCount")]
    [InlineData("expiredLeaseCount")]
    [InlineData("completedCount")]
    [InlineData("deadLetteredCount")]
    public void Snapshot_rejects_every_negative_count(string parameterName)
    {
        var exception = Should.Throw<ArgumentOutOfRangeException>(() =>
            SnapshotWithNegative(parameterName));

        exception.ParamName.ShouldBe(parameterName);
    }

    [Theory]
    [InlineData("readyUnmaterializedCount")]
    [InlineData("readyPendingCount")]
    public void Snapshot_rejects_ready_subset_overflow(string parameterName)
    {
        var exception = Should.Throw<ArgumentException>(() =>
            parameterName == "readyUnmaterializedCount"
                ? Snapshot(
                    captured: 1,
                    unmaterialized: 1,
                    readyUnmaterialized: 2,
                    oldestReadyAt: ObservedAt)
                : Snapshot(
                    captured: 1,
                    pending: 1,
                    readyPending: 2,
                    oldestReadyAt: ObservedAt));

        exception.ParamName.ShouldBe(parameterName);
    }

    [Fact]
    public void Snapshot_rejects_expired_subset_overflow()
    {
        var exception = Should.Throw<ArgumentException>(() => Snapshot(
            captured: 1,
            leased: 1,
            expiredLease: 2,
            oldestReadyAt: ObservedAt));

        exception.ParamName.ShouldBe("expiredLeaseCount");
    }

    [Fact]
    public void Snapshot_rejects_capture_tracking_mismatch()
    {
        var tooFew = Should.Throw<ArgumentException>(() => Snapshot(captured: 2, pending: 1));
        var tooMany = Should.Throw<ArgumentException>(() => Snapshot(captured: 1, pending: 2));

        tooFew.ParamName.ShouldBe("capturedCount");
        tooMany.ParamName.ShouldBe("capturedCount");
    }

    [Fact]
    public void Snapshot_rejects_inconsistent_oldest_ready_signal()
    {
        var unexpected = Should.Throw<ArgumentException>(() =>
            Snapshot(oldestReadyAt: ObservedAt));
        var missing = Should.Throw<ArgumentException>(() => Snapshot(
            captured: 1,
            unmaterialized: 1,
            readyUnmaterialized: 1));
        var future = Should.Throw<ArgumentOutOfRangeException>(() => Snapshot(
            captured: 1,
            pending: 1,
            readyPending: 1,
            oldestReadyAt: ObservedAt.AddTicks(1)));

        unexpected.ParamName.ShouldBe("oldestReadyAt");
        missing.ParamName.ShouldBe("oldestReadyAt");
        future.ParamName.ShouldBe("oldestReadyAt");
    }

    [Fact]
    public void Snapshot_rejects_inconsistent_next_lease_expiry_signal()
    {
        var unexpected = Should.Throw<ArgumentException>(() =>
            Snapshot(nextLeaseExpiry: ObservedAt.AddTicks(1)));
        var missing = Should.Throw<ArgumentException>(() => Snapshot(captured: 1, leased: 1));
        var exactBoundary = Should.Throw<ArgumentOutOfRangeException>(() => Snapshot(
            captured: 1,
            leased: 1,
            nextLeaseExpiry: ObservedAt));
        var past = Should.Throw<ArgumentOutOfRangeException>(() => Snapshot(
            captured: 1,
            leased: 1,
            nextLeaseExpiry: ObservedAt.AddTicks(-1)));

        unexpected.ParamName.ShouldBe("nextLeaseExpiry");
        missing.ParamName.ShouldBe("nextLeaseExpiry");
        exactBoundary.ParamName.ShouldBe("nextLeaseExpiry");
        past.ParamName.ShouldBe("nextLeaseExpiry");
    }

    [Fact]
    public void Snapshot_derived_counts_are_checked()
    {
        Should.Throw<OverflowException>(() => Snapshot(
            captured: long.MaxValue,
            pending: long.MaxValue,
            leased: 1,
            nextLeaseExpiry: ObservedAt.AddTicks(1)));

        Should.Throw<OverflowException>(() => Snapshot(
            captured: long.MaxValue,
            unmaterialized: long.MaxValue,
            readyUnmaterialized: long.MaxValue,
            pending: 1,
            readyPending: 1,
            oldestReadyAt: ObservedAt));
    }

    [Fact]
    public void Status_contract_is_narrow_immutable_and_payload_free()
    {
        typeof(DurableOutputStatusQuery).IsSealed.ShouldBeTrue();
        typeof(DurableOutputStatusSnapshot).IsSealed.ShouldBeTrue();

        AssertGetOnlyProperties(
            typeof(DurableOutputStatusQuery),
            (nameof(DurableOutputStatusQuery.ObservedAt), typeof(DateTimeOffset)));
        AssertGetOnlyProperties(
            typeof(DurableOutputStatusSnapshot),
            (nameof(DurableOutputStatusSnapshot.ObservedAt), typeof(DateTimeOffset)),
            (nameof(DurableOutputStatusSnapshot.CapturedCount), typeof(long)),
            (nameof(DurableOutputStatusSnapshot.UnmaterializedCount), typeof(long)),
            (nameof(DurableOutputStatusSnapshot.ReadyUnmaterializedCount), typeof(long)),
            (nameof(DurableOutputStatusSnapshot.PendingCount), typeof(long)),
            (nameof(DurableOutputStatusSnapshot.ReadyPendingCount), typeof(long)),
            (nameof(DurableOutputStatusSnapshot.LeasedCount), typeof(long)),
            (nameof(DurableOutputStatusSnapshot.ExpiredLeaseCount), typeof(long)),
            (nameof(DurableOutputStatusSnapshot.CompletedCount), typeof(long)),
            (nameof(DurableOutputStatusSnapshot.DeadLetteredCount), typeof(long)),
            (nameof(DurableOutputStatusSnapshot.OldestReadyAt), typeof(DateTimeOffset?)),
            (nameof(DurableOutputStatusSnapshot.NextLeaseExpiry), typeof(DateTimeOffset?)),
            (nameof(DurableOutputStatusSnapshot.TrackedDeliveryCount), typeof(long)),
            (nameof(DurableOutputStatusSnapshot.ReadyCount), typeof(long)));

        var method = typeof(IDurableOutputStatusStore).GetMethods().ShouldHaveSingleItem();
        method.Name.ShouldBe(nameof(IDurableOutputStatusStore.GetStatusAsync));
        method.ReturnType.ShouldBe(typeof(ValueTask<DurableOutputStatusSnapshot>));
        method.GetParameters().Select(parameter => parameter.ParameterType).ShouldBe(
            new[] { typeof(DurableOutputStatusQuery), typeof(CancellationToken) });
        method.GetParameters()[1].HasDefaultValue.ShouldBeTrue();
    }

    private static DurableOutputStatusSnapshot Snapshot(
        long captured = 0,
        long unmaterialized = 0,
        long readyUnmaterialized = 0,
        long pending = 0,
        long readyPending = 0,
        long leased = 0,
        long expiredLease = 0,
        long completed = 0,
        long deadLettered = 0,
        DateTimeOffset? oldestReadyAt = null,
        DateTimeOffset? nextLeaseExpiry = null)
        => new(
            ObservedAt,
            captured,
            unmaterialized,
            readyUnmaterialized,
            pending,
            readyPending,
            leased,
            expiredLease,
            completed,
            deadLettered,
            oldestReadyAt,
            nextLeaseExpiry);

    private static DurableOutputStatusSnapshot SnapshotWithNegative(string parameterName)
        => new(
            ObservedAt,
            capturedCount: parameterName == "capturedCount" ? -1 : 0,
            unmaterializedCount: parameterName == "unmaterializedCount" ? -1 : 0,
            readyUnmaterializedCount: parameterName == "readyUnmaterializedCount" ? -1 : 0,
            pendingCount: parameterName == "pendingCount" ? -1 : 0,
            readyPendingCount: parameterName == "readyPendingCount" ? -1 : 0,
            leasedCount: parameterName == "leasedCount" ? -1 : 0,
            expiredLeaseCount: parameterName == "expiredLeaseCount" ? -1 : 0,
            completedCount: parameterName == "completedCount" ? -1 : 0,
            deadLetteredCount: parameterName == "deadLetteredCount" ? -1 : 0,
            oldestReadyAt: null,
            nextLeaseExpiry: null);

    private static void AssertGetOnlyProperties(
        Type type,
        params (string Name, Type Type)[] expected)
    {
        var properties = type.GetProperties();

        properties.Select(property => (property.Name, property.PropertyType))
            .ShouldBe(expected);
        properties.ShouldAllBe(property => property.SetMethod == null);
    }
}
