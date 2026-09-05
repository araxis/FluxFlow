using FluxFlow.Components.Routing.Contracts;
using FluxFlow.Components.Routing.Nodes;
using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Routing.Tests;

public sealed class JoinPendingStoreTests
{
    [Fact]
    public void Pending_capacity_is_released_when_an_entry_is_matched()
    {
        var store = new JoinPendingStore<string, string>(StringComparer.Ordinal, 1);
        store.AddLeft("key", FlowMessage.Create("left"), DateTimeOffset.UtcNow);

        store.CanTrack.ShouldBeFalse();
        store.TryTakeLeft("key", out var left).ShouldBeTrue();

        left.Message.Value.ShouldBe("left");
        store.Count.ShouldBe(0);
        store.CanTrack.ShouldBeTrue();
    }

    [Fact]
    public void Opposite_side_entries_are_taken_in_fifo_order()
    {
        var store = new JoinPendingStore<string, string>(StringComparer.Ordinal, 3);
        var now = DateTimeOffset.Parse("2026-06-01T12:00:00Z");
        store.AddRight("key", FlowMessage.Create("first"), now);
        store.AddRight("key", FlowMessage.Create("second"), now.AddSeconds(1));

        store.TryTakeRight("key", out var first).ShouldBeTrue();
        store.TryTakeRight("key", out var second).ShouldBeTrue();

        first.Message.Value.ShouldBe("first");
        second.Message.Value.ShouldBe("second");
        store.Count.ShouldBe(0);
    }

    [Fact]
    public void Stale_deadlines_do_not_expire_a_later_entry()
    {
        var timeout = TimeSpan.FromSeconds(10);
        var firstReceivedAt = DateTimeOffset.Parse("2026-06-01T12:00:00Z");
        var secondReceivedAt = firstReceivedAt.AddSeconds(5);
        var store = new JoinPendingStore<string, string>(StringComparer.Ordinal, 2);
        store.AddLeft("key", FlowMessage.Create("first"), firstReceivedAt);
        store.TryTakeLeft("key", out _).ShouldBeTrue();
        store.AddLeft("key", FlowMessage.Create("second"), secondReceivedAt);

        store.GetNextDueAt(timeout).ShouldBe(secondReceivedAt + timeout);
        store.TakeExpired(firstReceivedAt + timeout, timeout, force: false).ShouldBeEmpty();

        var expired = store.TakeExpired(secondReceivedAt + timeout, timeout, force: false);
        expired.Count.ShouldBe(1);
        expired[0].Side.ShouldBe(FlowJoinSide.Left);
        expired[0].Left!.Message.Value.ShouldBe("second");
        store.Count.ShouldBe(0);
    }
}
