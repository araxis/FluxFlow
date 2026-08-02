using FluxFlow.Engine.DurableInput.Tests;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.DurableInput.SqlFile.Tests;

public sealed class SqlFileDurableInputStoreConcurrencyTests
{
    [Fact]
    public async Task Concurrent_identical_enqueue_has_one_winner_and_only_equivalent_duplicates()
    {
        using var database = TemporarySqliteDatabase.Create();
        var envelope = DurableInputStoreConformanceData.Envelope(messageId: "concurrent-same");
        await using var first = database.CreateStore();
        await using var second = database.CreateStore();
        await using var third = database.CreateStore();
        await using var fourth = database.CreateStore();

        var results = await Task.WhenAll(
            first.EnqueueAsync(envelope).AsTask(),
            second.EnqueueAsync(envelope).AsTask(),
            third.EnqueueAsync(envelope).AsTask(),
            fourth.EnqueueAsync(envelope).AsTask());

        results.Count(result => result.Status == DurableInputEnqueueStatus.Enqueued).ShouldBe(1);
        results.Count(result => result.Status == DurableInputEnqueueStatus.AlreadyExists).ShouldBe(3);
        results.ShouldAllBe(result => result.Key == envelope.Key);
        results.ShouldNotContain(result => result.Status == DurableInputEnqueueStatus.Conflict);
        var lease = (await first.LeaseAsync(DurableInputStoreConformanceData.Request())).Single();
        lease.Envelope.ShouldMatchEnvelope(envelope);
    }

    [Fact]
    public async Task Concurrent_conflicting_enqueue_has_one_winner_one_conflict_and_no_overwrite()
    {
        using var database = TemporarySqliteDatabase.Create();
        var firstEnvelope = DurableInputStoreConformanceData.Envelope(
            messageId: "concurrent-conflict",
            value: "first");
        var secondEnvelope = DurableInputStoreConformanceData.Envelope(
            messageId: "concurrent-conflict",
            value: "second");
        await using var first = database.CreateStore();
        await using var second = database.CreateStore();

        var results = await Task.WhenAll(
            first.EnqueueAsync(firstEnvelope).AsTask(),
            second.EnqueueAsync(secondEnvelope).AsTask());
        var retained = (await first.LeaseAsync(DurableInputStoreConformanceData.Request()))
            .Single()
            .Envelope;
        var winningEnvelope = results[0].Status == DurableInputEnqueueStatus.Enqueued
            ? firstEnvelope
            : secondEnvelope;

        results.Select(result => result.Status)
            .ShouldBe([
                DurableInputEnqueueStatus.Enqueued,
                DurableInputEnqueueStatus.Conflict
            ], ignoreOrder: true);
        retained.ShouldMatchEnvelope(winningEnvelope);
        retained.Payload.GetString().ShouldBe(winningEnvelope.Payload.GetString());
    }

    [Fact]
    public async Task Concurrent_multi_instance_leases_are_disjoint_and_cover_the_exact_due_batch()
    {
        using var database = TemporarySqliteDatabase.Create();
        await using var writer = database.CreateStore();
        var envelopes = Enumerable.Range(0, 12)
            .Select(index => DurableInputStoreConformanceData.Envelope(
                messageId: $"concurrent-lease-{index:D2}",
                enqueuedAt: DurableInputStoreConformanceData.Now.AddMinutes(-1)))
            .ToArray();
        foreach (var envelope in envelopes)
        {
            (await writer.EnqueueAsync(envelope)).Status.ShouldBe(DurableInputEnqueueStatus.Enqueued);
        }

        await using var first = database.CreateStore();
        await using var second = database.CreateStore();
        var pages = await Task.WhenAll(
            first.LeaseAsync(DurableInputStoreConformanceData.Request(
                ownerId: "owner-a",
                maxCount: envelopes.Length)).AsTask(),
            second.LeaseAsync(DurableInputStoreConformanceData.Request(
                ownerId: "owner-b",
                maxCount: envelopes.Length)).AsTask());
        var firstKeys = pages[0].Select(lease => lease.Envelope.Key).ToArray();
        var secondKeys = pages[1].Select(lease => lease.Envelope.Key).ToArray();
        var allKeys = firstKeys.Concat(secondKeys).ToArray();

        firstKeys.Intersect(secondKeys).ShouldBeEmpty();
        allKeys.ShouldBe(envelopes.Select(envelope => envelope.Key), ignoreOrder: true);
        allKeys.Distinct().Count().ShouldBe(envelopes.Length);
        pages[0].ShouldAllBe(static lease => lease.OwnerId == "owner-a");
        pages[1].ShouldAllBe(static lease => lease.OwnerId == "owner-b");
        pages.SelectMany(static page => page).ShouldAllBe(static lease => lease.Attempt == 1);
    }
}
