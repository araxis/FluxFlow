using System.Data;
using System.Diagnostics;
using FluxFlow.Engine.DurableOutput.Tests;
using Microsoft.Data.SqlClient;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.DurableOutput.TSql.IntegrationTests;

public sealed class TSqlDurableOutputConcurrencyTests
{
    [Fact]
    public async Task Equivalent_capture_across_stores_has_one_insert_and_no_duplicates()
    {
        await using var database = await TSqlTestDatabase.CreateAsync();
        var stores = CreateStores(database, 8);
        var envelope = TSqlDurableOutputTestSupport.ValueEnvelope("capture-equivalent");

        var results = await RaceAsync(stores.Select(store =>
            (Func<Task<DurableOutputEnqueueResult>>)(() => store.EnqueueAsync(envelope).AsTask())));

        results.Count(result => result.Status == DurableOutputEnqueueStatus.Enqueued).ShouldBe(1);
        results.Count(result => result.Status == DurableOutputEnqueueStatus.AlreadyExists).ShouldBe(7);
        results.Count(result => result.Status == DurableOutputEnqueueStatus.Conflict).ShouldBe(0);
        (await TSqlDurableOutputTestSupport.ScalarAsync<int>(
            database,
            "SELECT COUNT(*) FROM dbo.fluxflow_relational_outputs;"))
            .ShouldBe(1);
        var persisted = await stores[0].ReadAsync(envelope.Key);
        persisted.ShouldNotBeNull();
        persisted.ShouldMatchExactly(envelope);
    }

    [Fact]
    public async Task Conflicting_capture_across_stores_keeps_one_complete_winner_without_overwrite()
    {
        await using var database = await TSqlTestDatabase.CreateAsync();
        var stores = CreateStores(database, 8);
        var original = TSqlDurableOutputTestSupport.ValueEnvelope("capture-conflict");
        var changed = DurableOutputStoreConformanceData.MutateSameKey(
            original,
            DurableOutputContentMutation.Payload);
        var candidates = Enumerable.Range(0, stores.Count)
            .Select(index => index % 2 == 0 ? original : changed)
            .ToArray();

        var results = await RaceAsync(stores.Select((store, index) =>
            (Func<Task<DurableOutputEnqueueResult>>)(() =>
                store.EnqueueAsync(candidates[index]).AsTask())));

        results.Count(result => result.Status == DurableOutputEnqueueStatus.Enqueued).ShouldBe(1);
        results.Count(result => result.Status == DurableOutputEnqueueStatus.AlreadyExists).ShouldBe(3);
        results.Count(result => result.Status == DurableOutputEnqueueStatus.Conflict).ShouldBe(4);
        (await TSqlDurableOutputTestSupport.ScalarAsync<int>(
            database,
            "SELECT COUNT(*) FROM dbo.fluxflow_relational_outputs;"))
            .ShouldBe(1);

        var persisted = await stores[0].ReadAsync(original.Key);
        persisted.ShouldNotBeNull();
        var winner = results.Single(result => result.Status == DurableOutputEnqueueStatus.Enqueued);
        var expected = ReferenceEquals(winner, results[0])
            ? candidates[0]
            : candidates[Array.IndexOf(results.ToArray(), winner)];
        persisted.ShouldMatchExactly(expected);
    }

    [Fact]
    public async Task Concurrent_lease_of_many_rows_never_returns_the_same_key_twice()
    {
        await using var database = await TSqlTestDatabase.CreateAsync();
        await using var writer = database.CreateStore();
        var envelopes = Enumerable.Range(0, 8)
            .Select(index => TSqlDurableOutputTestSupport.ValueEnvelope($"lease-many-{index:D2}"))
            .ToArray();
        foreach (var envelope in envelopes)
            (await writer.EnqueueAsync(envelope)).Status.ShouldBe(DurableOutputEnqueueStatus.Enqueued);
        var stores = CreateStores(database, envelopes.Length);

        var leases = await RaceAsync(stores.Select((store, index) =>
            (Func<Task<DurableOutputDeliveryLease?>>)(() => store.TryLeaseAsync(
                TSqlDurableOutputTestSupport.Request(
                    TSqlDurableOutputTestSupport.Now,
                    $"many-worker-{index}")).AsTask())));

        leases.Count(static lease => lease is not null).ShouldBe(envelopes.Length);
        var keys = leases.Select(static lease => lease!.Envelope.Key).ToArray();
        keys.Distinct().Count().ShouldBe(envelopes.Length);
        keys.OrderBy(static key => key.MessageId.ToString(), StringComparer.Ordinal)
            .ShouldBe(envelopes.Select(static envelope => envelope.Key));
        (await TSqlDurableOutputTestSupport.ScalarAsync<int>(
            database,
            "SELECT COUNT(*) FROM dbo.fluxflow_relational_output_deliveries WHERE state = 2 AND attempt = 1;"))
            .ShouldBe(envelopes.Length);
    }

    [Fact]
    public async Task Concurrent_completion_applies_once_and_persists_one_tombstone()
    {
        await using var database = await TSqlTestDatabase.CreateAsync();
        await using var owner = database.CreateStore();
        var envelope = TSqlDurableOutputTestSupport.ValueEnvelope("settle-complete");
        var lease = await TSqlDurableOutputTestSupport.CaptureAndLeaseAsync(
            owner,
            envelope,
            TSqlDurableOutputTestSupport.Now);
        var stores = CreateStores(database, 2);
        var completedAt = TSqlDurableOutputTestSupport.Now.AddSeconds(10);
        var transition = new DurableOutputDeliveryTransition(
            envelope.Key,
            lease.LeaseToken,
            completedAt);

        var results = await RaceAsync(stores.Select(store =>
            (Func<Task<DurableOutputDeliveryTransitionResult>>)(() =>
                store.CompleteAsync(transition).AsTask())));

        results.Select(static result => result.Status)
            .OrderBy(static status => status)
            .ShouldBe([
                DurableOutputDeliveryTransitionStatus.Applied,
                DurableOutputDeliveryTransitionStatus.InvalidState
            ]);
        var rows = await TSqlDurableOutputTestSupport.ReadStringsAsync(
            database,
            "SELECT state, attempt, delivered_at_utc_ticks, lease_token, lease_owner FROM dbo.fluxflow_relational_output_deliveries;");
        rows.ShouldBe([$"3|1|{completedAt.UtcTicks}|<null>|<null>"]);
    }

    [Fact]
    public async Task Concurrent_replay_applies_once_and_preserves_generation()
    {
        await using var database = await TSqlTestDatabase.CreateAsync();
        await using var owner = database.CreateStore();
        var envelope = TSqlDurableOutputTestSupport.ValueEnvelope("settle-replay");
        var details = await TSqlDurableOutputTestSupport.CaptureAndDeadLetterAsync(
            owner,
            envelope,
            TSqlDurableOutputTestSupport.Now,
            TSqlDurableOutputTestSupport.Now.AddSeconds(10));
        var stores = CreateStores(database, 2);
        var replayedAt = TSqlDurableOutputTestSupport.Now.AddSeconds(20);
        var nextAttemptAt = TSqlDurableOutputTestSupport.Now.AddMinutes(2);
        var replay = new DurableOutputReplay(
            envelope.Key,
            details.Generation,
            replayedAt,
            nextAttemptAt);

        var results = await RaceAsync(stores.Select(store =>
            (Func<Task<DurableOutputReplayResult>>)(() => store.ReplayAsync(replay).AsTask())));

        results.Select(static result => result.Status)
            .OrderBy(static status => status)
            .ShouldBe([DurableOutputReplayStatus.Replayed, DurableOutputReplayStatus.NotDeadLettered]);
        var rows = await TSqlDurableOutputTestSupport.ReadStringsAsync(
            database,
            "SELECT state, attempt, next_attempt_utc_ticks, dead_letter_generation, dead_letter_reason FROM dbo.fluxflow_relational_output_deliveries;");
        rows.ShouldBe([$"1|0|{nextAttemptAt.UtcTicks}|1|<null>"]);
    }

    [Fact]
    public async Task External_row_lock_times_out_without_partial_settlement_and_recovers_after_rollback()
    {
        await using var database = await TSqlTestDatabase.CreateAsync();
        await using var store = database.CreateStore(new TSqlDurableOutputStoreOptions
        {
            ConnectionString = database.ConnectionString,
            CommandTimeout = TimeSpan.FromSeconds(5)
        });
        var envelope = TSqlDurableOutputTestSupport.ValueEnvelope("external-lock");
        var lease = await TSqlDurableOutputTestSupport.CaptureAndLeaseAsync(
            store,
            envelope,
            TSqlDurableOutputTestSupport.Now,
            "lock-owner");
        var completedAt = TSqlDurableOutputTestSupport.Now.AddSeconds(10);
        var transition = new DurableOutputDeliveryTransition(
            envelope.Key,
            lease.LeaseToken,
            completedAt);

        await using (var lockConnection = await database.OpenConnectionAsync())
        await using (var transaction = await lockConnection.BeginTransactionAsync())
        {
            await using var lockCommand = lockConnection.CreateCommand();
            lockCommand.Transaction = (SqlTransaction)transaction;
            lockCommand.CommandText = """
                UPDATE dbo.fluxflow_relational_output_deliveries
                SET attempt = attempt
                WHERE application_address = @address AND message_id = @message;
                """;
            AddKey(lockCommand, envelope);
            (await lockCommand.ExecuteNonQueryAsync()).ShouldBe(1);

            var stopwatch = Stopwatch.StartNew();
            var exception = await Should.ThrowAsync<SqlException>(
                () => store.CompleteAsync(transition).AsTask());
            stopwatch.Stop();
            exception.Number.ShouldBe(-2);
            stopwatch.Elapsed.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromSeconds(4));
            stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(15));
            await transaction.RollbackAsync();
        }

        var beforeRecovery = await TSqlDurableOutputTestSupport.ReadStringsAsync(
            database,
            "SELECT state, attempt, lease_owner, CONVERT(nvarchar(36), lease_token) FROM dbo.fluxflow_relational_output_deliveries;");
        beforeRecovery.ShouldBe([
            $"2|1|{lease.OwnerId}|{lease.LeaseToken.ToString("D").ToUpperInvariant()}"
        ]);
        (await store.CompleteAsync(transition)).Status.ShouldBe(DurableOutputDeliveryTransitionStatus.Applied);
        (await TSqlDurableOutputTestSupport.ScalarAsync<int>(
            database,
            "SELECT state FROM dbo.fluxflow_relational_output_deliveries;"))
            .ShouldBe(3);
    }

    private static IReadOnlyList<TSqlDurableOutputStore> CreateStores(
        TSqlTestDatabase database,
        int count)
        => Enumerable.Range(0, count).Select(_ => database.CreateStore()).ToArray();

    private static async Task<IReadOnlyList<T>> RaceAsync<T>(
        IEnumerable<Func<Task<T>>> operations)
    {
        var operationArray = operations.ToArray();
        using var ready = new CountdownEvent(operationArray.Length);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = operationArray.Select(operation => Task.Run(async () =>
        {
            ready.Signal();
            await start.Task;
            return await operation();
        })).ToArray();

        ready.Wait(TimeSpan.FromSeconds(10)).ShouldBeTrue();
        start.SetResult();
        return await Task.WhenAll(tasks);
    }

    private static void AddKey(SqlCommand command, DurableOutputEnvelope envelope)
    {
        TSqlDurableOutputTestSupport.AddKeyParameter(
            command,
            "@address",
            envelope.Key.Address.ToString(),
            300);
        TSqlDurableOutputTestSupport.AddKeyParameter(
            command,
            "@message",
            envelope.Key.MessageId.ToString(),
            128);
    }
}
