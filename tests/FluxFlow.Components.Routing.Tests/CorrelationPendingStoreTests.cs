using FluxFlow.Components.Routing.Nodes;
using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Routing.Tests;

public sealed class CorrelationPendingStoreTests
{
    [Fact]
    public void Capacity_is_released_when_a_pending_key_is_removed()
    {
        var store = new CorrelationPendingStore<string>(StringComparer.Ordinal, 1);

        store.TryGetOrCreate("first", out _, out var firstCreated).ShouldBeTrue();
        firstCreated.ShouldBeTrue();
        store.TryGetOrCreate("second", out _, out _).ShouldBeFalse();

        store.Remove("first").ShouldBeTrue();
        store.TryGetOrCreate("second", out _, out var secondCreated).ShouldBeTrue();
        secondCreated.ShouldBeTrue();
    }

    [Fact]
    public void Stale_deadlines_do_not_expire_a_reused_key()
    {
        var timeout = TimeSpan.FromSeconds(10);
        var firstReceivedAt = DateTimeOffset.Parse("2026-06-01T12:00:00Z");
        var secondReceivedAt = firstReceivedAt.AddSeconds(5);
        var store = new CorrelationPendingStore<string>(StringComparer.Ordinal, 2);

        AddRequest(store, "key", "first", firstReceivedAt);
        store.Remove("key").ShouldBeTrue();
        AddRequest(store, "key", "second", secondReceivedAt);

        store.GetNextDueAt(timeout).ShouldBe(secondReceivedAt + timeout);
        store.TakeExpired(firstReceivedAt + timeout, timeout, force: false).ShouldBeEmpty();

        var expired = store.TakeExpired(secondReceivedAt + timeout, timeout, force: false);
        expired.Count.ShouldBe(1);
        expired[0].Key.ShouldBe("key");
        expired[0].Pending.Request!.Message.Value.ShouldBe("second");
    }

    [Fact]
    public void Forced_expiry_returns_each_pending_side_once()
    {
        var receivedAt = DateTimeOffset.Parse("2026-06-01T12:00:00Z");
        var store = new CorrelationPendingStore<string>(StringComparer.Ordinal, 2);
        store.TryGetOrCreate("key", out var pair, out _).ShouldBeTrue();
        pair.Set(
            "request",
            new CorrelationPendingEntry<string>(
                FlowMessage.Create("left"),
                "request",
                receivedAt),
            "request",
            StringComparer.Ordinal);
        pair.Set(
            "response",
            new CorrelationPendingEntry<string>(
                FlowMessage.Create("right"),
                "response",
                receivedAt.AddSeconds(1)),
            "request",
            StringComparer.Ordinal);
        store.TrackDeadline("key", receivedAt);

        var expired = store.TakeExpired(receivedAt, TimeSpan.FromMinutes(1), force: true);

        expired.Single().Pending.Entries.Select(entry => entry.Message.Value)
            .ShouldBe(["left", "right"]);
        store.TakeExpired(receivedAt, TimeSpan.Zero, force: true).ShouldBeEmpty();
    }

    private static void AddRequest(
        CorrelationPendingStore<string> store,
        string key,
        string value,
        DateTimeOffset receivedAt)
    {
        store.TryGetOrCreate(key, out var pair, out _).ShouldBeTrue();
        pair.Set(
            "request",
            new CorrelationPendingEntry<string>(
                FlowMessage.Create(value),
                "request",
                receivedAt),
            "request",
            StringComparer.Ordinal);
        store.TrackDeadline(key, receivedAt);
    }
}
