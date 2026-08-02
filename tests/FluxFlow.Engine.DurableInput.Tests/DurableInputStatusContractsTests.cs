using Shouldly;
using Xunit;

namespace FluxFlow.Engine.DurableInput.Tests;

public sealed class DurableInputStatusContractsTests
{
    private static readonly DateTimeOffset ObservedAt =
        new(2026, 8, 1, 12, 30, 0, TimeSpan.FromHours(2));

    [Fact]
    public void Query_preserves_exact_observed_at_and_offset()
    {
        var query = new DurableInputStatusQuery(ObservedAt);

        query.ObservedAt.ShouldBe(ObservedAt);
        query.ObservedAt.Offset.ShouldBe(TimeSpan.FromHours(2));
    }

    [Fact]
    public void Snapshot_preserves_valid_mixed_values_and_computes_checked_total()
    {
        var oldestReadyAt = ObservedAt.AddMinutes(-40);
        var nextLeaseExpiry = ObservedAt.AddMinutes(20);

        var snapshot = new DurableInputStatusSnapshot(
            ObservedAt,
            pendingCount: 3,
            readyPendingCount: 2,
            leasedCount: 4,
            expiredLeaseCount: 1,
            deliveredCount: 5,
            deadLetteredCount: 6,
            oldestReadyAt,
            nextLeaseExpiry);

        snapshot.ObservedAt.ShouldBe(ObservedAt);
        snapshot.PendingCount.ShouldBe(3);
        snapshot.ReadyPendingCount.ShouldBe(2);
        snapshot.LeasedCount.ShouldBe(4);
        snapshot.ExpiredLeaseCount.ShouldBe(1);
        snapshot.DeliveredCount.ShouldBe(5);
        snapshot.DeadLetteredCount.ShouldBe(6);
        snapshot.OldestReadyAt.ShouldBe(oldestReadyAt);
        snapshot.NextLeaseExpiry.ShouldBe(nextLeaseExpiry);
        snapshot.TotalCount.ShouldBe(18);
    }

    [Fact]
    public void Snapshot_accepts_each_valid_readiness_and_expiry_shape()
    {
        var snapshots = new[]
        {
            Snapshot(),
            Snapshot(pending: 1),
            Snapshot(pending: 1, readyPending: 1, oldestReadyAt: ObservedAt),
            Snapshot(leased: 1, nextLeaseExpiry: ObservedAt.AddTicks(1)),
            Snapshot(leased: 1, expiredLease: 1, oldestReadyAt: ObservedAt),
            Snapshot(delivered: 1),
            Snapshot(deadLettered: 1)
        };

        snapshots.Select(snapshot => snapshot.TotalCount)
            .ShouldBe(new long[] { 0, 1, 1, 1, 1, 1, 1 });
        snapshots[2].OldestReadyAt.ShouldBe(ObservedAt);
        snapshots[3].NextLeaseExpiry.ShouldBe(ObservedAt.AddTicks(1));
        snapshots[4].OldestReadyAt.ShouldBe(ObservedAt);
    }

    [Theory]
    [InlineData("pendingCount")]
    [InlineData("readyPendingCount")]
    [InlineData("leasedCount")]
    [InlineData("expiredLeaseCount")]
    [InlineData("deliveredCount")]
    [InlineData("deadLetteredCount")]
    public void Snapshot_rejects_every_negative_count(string parameterName)
    {
        var exception = Should.Throw<ArgumentOutOfRangeException>(() =>
            SnapshotWithNegative(parameterName));

        exception.ParamName.ShouldBe(parameterName);
    }

    [Theory]
    [InlineData("readyPendingCount")]
    [InlineData("expiredLeaseCount")]
    public void Snapshot_rejects_ready_or_expired_subset_overflow(string parameterName)
    {
        var exception = Should.Throw<ArgumentException>(() =>
            parameterName == "readyPendingCount"
                ? Snapshot(pending: 1, readyPending: 2, oldestReadyAt: ObservedAt)
                : Snapshot(leased: 1, expiredLease: 2, oldestReadyAt: ObservedAt));

        exception.ParamName.ShouldBe(parameterName);
    }

    [Fact]
    public void Snapshot_rejects_inconsistent_oldest_ready_signal()
    {
        var unexpected = Should.Throw<ArgumentException>(() =>
            Snapshot(oldestReadyAt: ObservedAt));
        var missing = Should.Throw<ArgumentException>(() =>
            Snapshot(pending: 1, readyPending: 1));
        var future = Should.Throw<ArgumentOutOfRangeException>(() =>
            Snapshot(
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
        var missing = Should.Throw<ArgumentException>(() => Snapshot(leased: 1));
        var exactBoundary = Should.Throw<ArgumentOutOfRangeException>(() =>
            Snapshot(leased: 1, nextLeaseExpiry: ObservedAt));
        var past = Should.Throw<ArgumentOutOfRangeException>(() =>
            Snapshot(leased: 1, nextLeaseExpiry: ObservedAt.AddTicks(-1)));

        unexpected.ParamName.ShouldBe("nextLeaseExpiry");
        missing.ParamName.ShouldBe("nextLeaseExpiry");
        exactBoundary.ParamName.ShouldBe("nextLeaseExpiry");
        past.ParamName.ShouldBe("nextLeaseExpiry");
    }

    [Fact]
    public void Snapshot_total_is_checked()
    {
        var snapshot = Snapshot(pending: long.MaxValue, delivered: 1);

        Should.Throw<OverflowException>(() => _ = snapshot.TotalCount);
    }

    [Fact]
    public void Status_contract_is_narrow_immutable_and_payload_free()
    {
        typeof(DurableInputStatusQuery).IsSealed.ShouldBeTrue();
        typeof(DurableInputStatusSnapshot).IsSealed.ShouldBeTrue();

        AssertGetOnlyProperties(
            typeof(DurableInputStatusQuery),
            (nameof(DurableInputStatusQuery.ObservedAt), typeof(DateTimeOffset)));
        AssertGetOnlyProperties(
            typeof(DurableInputStatusSnapshot),
            (nameof(DurableInputStatusSnapshot.ObservedAt), typeof(DateTimeOffset)),
            (nameof(DurableInputStatusSnapshot.PendingCount), typeof(long)),
            (nameof(DurableInputStatusSnapshot.ReadyPendingCount), typeof(long)),
            (nameof(DurableInputStatusSnapshot.LeasedCount), typeof(long)),
            (nameof(DurableInputStatusSnapshot.ExpiredLeaseCount), typeof(long)),
            (nameof(DurableInputStatusSnapshot.DeliveredCount), typeof(long)),
            (nameof(DurableInputStatusSnapshot.DeadLetteredCount), typeof(long)),
            (nameof(DurableInputStatusSnapshot.OldestReadyAt), typeof(DateTimeOffset?)),
            (nameof(DurableInputStatusSnapshot.NextLeaseExpiry), typeof(DateTimeOffset?)),
            (nameof(DurableInputStatusSnapshot.TotalCount), typeof(long)));

        var method = typeof(IDurableInputStatusStore).GetMethods().ShouldHaveSingleItem();
        method.Name.ShouldBe(nameof(IDurableInputStatusStore.GetStatusAsync));
        method.ReturnType.ShouldBe(typeof(ValueTask<DurableInputStatusSnapshot>));
        method.GetParameters().Select(parameter => parameter.ParameterType).ShouldBe(
            new[] { typeof(DurableInputStatusQuery), typeof(CancellationToken) });
        method.GetParameters()[1].HasDefaultValue.ShouldBeTrue();
    }

    private static DurableInputStatusSnapshot Snapshot(
        long pending = 0,
        long readyPending = 0,
        long leased = 0,
        long expiredLease = 0,
        long delivered = 0,
        long deadLettered = 0,
        DateTimeOffset? oldestReadyAt = null,
        DateTimeOffset? nextLeaseExpiry = null)
        => new(
            ObservedAt,
            pending,
            readyPending,
            leased,
            expiredLease,
            delivered,
            deadLettered,
            oldestReadyAt,
            nextLeaseExpiry);

    private static DurableInputStatusSnapshot SnapshotWithNegative(string parameterName)
        => new(
            ObservedAt,
            pendingCount: parameterName == "pendingCount" ? -1 : 0,
            readyPendingCount: parameterName == "readyPendingCount" ? -1 : 0,
            leasedCount: parameterName == "leasedCount" ? -1 : 0,
            expiredLeaseCount: parameterName == "expiredLeaseCount" ? -1 : 0,
            deliveredCount: parameterName == "deliveredCount" ? -1 : 0,
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
