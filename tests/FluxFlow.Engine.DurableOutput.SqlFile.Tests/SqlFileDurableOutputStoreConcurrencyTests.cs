using FluxFlow.Engine.DurableOutput.Tests;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.DurableOutput.SqlFile.Tests;

public sealed class SqlFileDurableOutputStoreConcurrencyTests
{
    [Fact]
    public async Task Concurrent_identical_enqueue_has_one_winner_and_only_equivalent_duplicates()
    {
        using var database = TemporarySqliteDatabase.Create();
        var envelope = DurableOutputStoreConformanceData.Envelope(messageId: "concurrent-same");
        await using var first = database.CreateStore();
        await using var second = database.CreateStore();
        await using var third = database.CreateStore();
        await using var fourth = database.CreateStore();

        var results = await Task.WhenAll(
            first.EnqueueAsync(envelope).AsTask(),
            second.EnqueueAsync(envelope).AsTask(),
            third.EnqueueAsync(envelope).AsTask(),
            fourth.EnqueueAsync(envelope).AsTask());

        results.Count(result => result.Status == DurableOutputEnqueueStatus.Enqueued).ShouldBe(1);
        results.Count(result => result.Status == DurableOutputEnqueueStatus.AlreadyExists).ShouldBe(3);
        results.ShouldAllBe(result => result.Key == envelope.Key);
        results.ShouldNotContain(result => result.Status == DurableOutputEnqueueStatus.Conflict);
        (await SqlFileDurableOutputTestDatabase.ReadOutputAsync(
            database.DatabasePath,
            envelope.Key)).ShouldNotBeNull().ShouldMatchExactly(envelope);
    }

    [Fact]
    public async Task Concurrent_conflicting_enqueue_has_one_winner_one_conflict_and_no_overwrite()
    {
        using var database = TemporarySqliteDatabase.Create();
        var firstEnvelope = DurableOutputStoreConformanceData.Envelope(
            messageId: "concurrent-conflict",
            payload: System.Text.Json.JsonSerializer.SerializeToElement("first"));
        var secondEnvelope = DurableOutputStoreConformanceData.Envelope(
            messageId: "concurrent-conflict",
            payload: System.Text.Json.JsonSerializer.SerializeToElement("second"));
        await using var first = database.CreateStore();
        await using var second = database.CreateStore();

        var results = await Task.WhenAll(
            first.EnqueueAsync(firstEnvelope).AsTask(),
            second.EnqueueAsync(secondEnvelope).AsTask());
        var winner = results[0].Status == DurableOutputEnqueueStatus.Enqueued
            ? firstEnvelope
            : secondEnvelope;
        var retained = await SqlFileDurableOutputTestDatabase.ReadOutputAsync(
            database.DatabasePath,
            winner.Key);

        results.Select(result => result.Status).ShouldBe([
            DurableOutputEnqueueStatus.Enqueued,
            DurableOutputEnqueueStatus.Conflict
        ], ignoreOrder: true);
        retained.ShouldNotBeNull().ShouldMatchExactly(winner);
    }

    [Fact]
    public async Task Concurrent_different_keys_all_enqueue_once_without_loss_or_conflict()
    {
        using var database = TemporarySqliteDatabase.Create();
        var envelopes = Enumerable.Range(0, 16)
            .Select(index => DurableOutputStoreConformanceData.Envelope(
                messageId: $"concurrent-{index:D2}"))
            .ToArray();
        var stores = envelopes.Select(_ => database.CreateStore()).ToArray();
        try
        {
            var tasks = envelopes.Select((envelope, index) =>
                stores[index].EnqueueAsync(envelope).AsTask());

            var results = await Task.WhenAll(tasks);

            results.ShouldAllBe(result => result.Status == DurableOutputEnqueueStatus.Enqueued);
            results.Select(result => result.Key).ShouldBe(
                envelopes.Select(envelope => envelope.Key),
                ignoreOrder: true);
            await using var connection = await SqlFileDurableOutputTestDatabase.OpenAsync(
                database.DatabasePath);
            (await SqlFileDurableOutputTestDatabase.ScalarAsync<long>(
                connection,
                "SELECT COUNT(*) FROM fluxflow_durable_outputs;")).ShouldBe(envelopes.Length);
        }
        finally
        {
            foreach (var store in stores)
                await store.DisposeAsync();
        }
    }
}
