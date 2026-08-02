using System.Diagnostics;
using FluxFlow.Engine.DurableOutput.Tests;
using Microsoft.Data.Sqlite;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.DurableOutput.SqlFile.Tests;

public sealed class SqlFileDurableOutputDeadLetterInfrastructureTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 1, 12, 0, 0, TimeSpan.FromHours(2));

    [Fact]
    public async Task Dead_letter_settlement_persists_exact_sqlite_state_encoding()
    {
        using var database = TemporarySqliteDatabase.Create();
        var envelope = DurableOutputStoreConformanceData.ErrorEnvelope("sqlite-dead-state");
        await using var store = database.CreateStore();

        (await CreateDeadLetterAsync(store, envelope, Now, Now.AddSeconds(1))).Status
            .ShouldBe(DurableOutputDeliveryTransitionStatus.Applied);

        await using var connection = await SqlFileDurableOutputTestDatabase.OpenAsync(
            database.DatabasePath);
        (await SqlFileDurableOutputTestDatabase.ReadStringsAsync(
            connection,
            """
            SELECT state || '|' || attempt || '|' || dead_letter_reason || '|' ||
                   dead_letter_generation || '|' ||
                   (lease_token IS NULL) || '|' || (lease_owner IS NULL)
            FROM fluxflow_durable_output_deliveries;
            """)).ShouldBe(["4|1|1|1|1|1"]);
    }

    [Fact]
    public async Task Concurrent_sqlite_store_instances_dead_letter_one_persisted_winner()
    {
        using var database = TemporarySqliteDatabase.Create();
        var envelope = DurableOutputStoreConformanceData.Envelope("sqlite-concurrent-dead");
        await using var owner = database.CreateStore();
        await owner.EnqueueAsync(envelope);
        var lease = (await owner.TryLeaseAsync(Request(Now))).ShouldNotBeNull();
        await using var first = database.CreateStore();
        await using var second = database.CreateStore();
        var transition = DeadLetter(envelope.Key, lease.LeaseToken, Now.AddSeconds(1));
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var operations = new[]
        {
            Task.Run(async () =>
            {
                await start.Task;
                return await first.DeadLetterAsync(transition);
            }),
            Task.Run(async () =>
            {
                await start.Task;
                return await second.DeadLetterAsync(transition);
            })
        };

        start.TrySetResult();
        var results = await Task.WhenAll(operations);

        results.Count(static result =>
            result.Status == DurableOutputDeliveryTransitionStatus.Applied).ShouldBe(1);
        results.Count(static result =>
            result.Status == DurableOutputDeliveryTransitionStatus.InvalidState).ShouldBe(1);
        await using var connection = await SqlFileDurableOutputTestDatabase.OpenAsync(
            database.DatabasePath);
        (await SqlFileDurableOutputTestDatabase.ScalarAsync<long>(
            connection,
            "SELECT COUNT(*) FROM fluxflow_durable_output_deliveries WHERE state = 4 AND dead_letter_generation = 1;"))
            .ShouldBe(1);
    }

    [Fact]
    public async Task Concurrent_sqlite_store_instances_replay_one_persisted_winner()
    {
        using var database = TemporarySqliteDatabase.Create();
        var envelope = DurableOutputStoreConformanceData.Envelope("sqlite-concurrent-replay");
        await using var owner = database.CreateStore();
        await CreateDeadLetterAsync(owner, envelope, Now, Now.AddSeconds(1));
        await using var first = database.CreateStore();
        await using var second = database.CreateStore();
        var replay = new DurableOutputReplay(
            envelope.Key,
            1,
            Now.AddMinutes(1),
            Now.AddMinutes(1));
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var operations = new[]
        {
            Task.Run(async () =>
            {
                await start.Task;
                return await first.ReplayAsync(replay);
            }),
            Task.Run(async () =>
            {
                await start.Task;
                return await second.ReplayAsync(replay);
            })
        };

        start.TrySetResult();
        var results = await Task.WhenAll(operations);

        results.Count(static result => result.Status == DurableOutputReplayStatus.Replayed)
            .ShouldBe(1);
        results.Count(static result => result.Status == DurableOutputReplayStatus.NotDeadLettered)
            .ShouldBe(1);
        await using var connection = await SqlFileDurableOutputTestDatabase.OpenAsync(
            database.DatabasePath);
        (await SqlFileDurableOutputTestDatabase.ReadStringsAsync(
            connection,
            """
            SELECT state || '|' || attempt || '|' || dead_letter_generation || '|' ||
                   (dead_letter_reason IS NULL) || '|' ||
                   (dead_lettered_at_utc_ticks IS NULL)
            FROM fluxflow_durable_output_deliveries;
            """)).ShouldBe(["1|0|1|1|1"]);
    }

    [Fact]
    public async Task Dead_letter_generation_and_replayed_schedule_survive_reopen()
    {
        using var database = TemporarySqliteDatabase.Create();
        var envelope = DurableOutputStoreConformanceData.ErrorEnvelope("reopen-dead-letter");
        await using (var writer = database.CreateStore())
            await CreateDeadLetterAsync(writer, envelope, Now, Now.AddSeconds(1));

        var replayAt = Now.AddMinutes(1);
        var due = replayAt.AddMinutes(1);
        await using (var replayer = database.CreateStore(
            createDatabase: false,
            createDirectory: false))
        {
            var details = (await replayer.GetAsync(envelope.Key)).ShouldNotBeNull();
            details.Envelope.HasSameContent(envelope).ShouldBeTrue();
            details.Generation.ShouldBe(1);
            (await replayer.ReplayAsync(new DurableOutputReplay(
                envelope.Key,
                1,
                replayAt,
                due))).Status.ShouldBe(DurableOutputReplayStatus.Replayed);
        }

        await using var reopened = database.CreateStore(createDatabase: false, createDirectory: false);
        (await reopened.TryLeaseAsync(Request(due.AddTicks(-1), "reopen-early"))).ShouldBeNull();
        var lease = (await reopened.TryLeaseAsync(Request(due, "reopen-due"))).ShouldNotBeNull();
        lease.Envelope.HasSameContent(envelope).ShouldBeTrue();
        lease.Attempt.ShouldBe(1);
        lease.LeasedAt.UtcTicks.ShouldBe(due.UtcTicks);
        lease.LeasedAt.Offset.ShouldBe(due.Offset);
    }

    [Fact]
    public async Task Dead_letter_operations_bound_busy_timeout_name_action_and_recover()
    {
        using var database = TemporarySqliteDatabase.Create();
        var timeout = TimeSpan.FromMilliseconds(150);
        var envelope = DurableOutputStoreConformanceData.Envelope("busy-replay");
        await using var store = database.CreateStore(busyTimeout: timeout);
        await CreateDeadLetterAsync(store, envelope, Now, Now.AddSeconds(1));
        await using var lockConnection = await SqlFileDurableOutputTestDatabase.OpenAsync(
            database.DatabasePath);
        await using var writeLock = lockConnection.BeginTransaction(deferred: false);
        var stopwatch = Stopwatch.StartNew();

        var exception = await Should.ThrowAsync<InvalidOperationException>(() => store.ReplayAsync(
            new DurableOutputReplay(envelope.Key, 1, Now.AddMinutes(1), Now.AddMinutes(1)))
            .AsTask());
        stopwatch.Stop();

        exception.Message.ShouldContain("dead-letter replay");
        exception.Message.ShouldContain("configured busy timeout");
        exception.InnerException.ShouldBeOfType<SqliteException>();
        stopwatch.Elapsed.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(100));
        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5));
        await writeLock.RollbackAsync();
        (await store.ReplayAsync(new DurableOutputReplay(
            envelope.Key,
            1,
            Now.AddMinutes(1),
            Now.AddMinutes(1)))).Status.ShouldBe(DurableOutputReplayStatus.Replayed);
    }

    [Fact]
    public async Task List_get_and_replay_reject_corrupt_dead_letter_rows_without_repair()
    {
        using var database = TemporarySqliteDatabase.Create();
        var envelope = DurableOutputStoreConformanceData.Envelope("corrupt-dead-letter");
        await using var store = database.CreateStore();
        await CreateDeadLetterAsync(store, envelope, Now, Now.AddSeconds(1));
        await SqlFileDurableOutputTestDatabase.ExecuteAsync(
            database.DatabasePath,
            """
            PRAGMA ignore_check_constraints = ON;
            UPDATE fluxflow_durable_output_deliveries
            SET dead_letter_generation = 0;
            """);

        (await Should.ThrowAsync<InvalidDataException>(() =>
            store.ListAsync(new DurableOutputDeadLetterQuery()).AsTask()))
            .Message.ShouldContain("generation");
        (await Should.ThrowAsync<InvalidDataException>(() => store.GetAsync(envelope.Key).AsTask()))
            .Message.ShouldContain("generation");
        (await Should.ThrowAsync<InvalidDataException>(() => store.ReplayAsync(
            new DurableOutputReplay(envelope.Key, 1, Now.AddMinutes(1), Now.AddMinutes(1)))
            .AsTask())).Message.ShouldContain("generation");
        await using var connection = await SqlFileDurableOutputTestDatabase.OpenAsync(
            database.DatabasePath);
        (await SqlFileDurableOutputTestDatabase.ScalarAsync<long>(
            connection,
            "SELECT dead_letter_generation FROM fluxflow_durable_output_deliveries;"))
            .ShouldBe(0);
    }

    private static async ValueTask<DurableOutputDeliveryTransitionResult> CreateDeadLetterAsync(
        SqlFileDurableOutputStore store,
        DurableOutputEnvelope envelope,
        DateTimeOffset leaseAt,
        DateTimeOffset deadLetteredAt)
    {
        (await store.EnqueueAsync(envelope)).Status.ShouldBe(DurableOutputEnqueueStatus.Enqueued);
        var lease = (await store.TryLeaseAsync(Request(leaseAt))).ShouldNotBeNull();
        lease.Envelope.Key.ShouldBe(envelope.Key);
        return await store.DeadLetterAsync(DeadLetter(
            envelope.Key,
            lease.LeaseToken,
            deadLetteredAt));
    }

    private static DurableOutputDeliveryDeadLetter DeadLetter(
        DurableOutputKey key,
        Guid leaseToken,
        DateTimeOffset deadLetteredAt)
        => DurableOutputStoreConformanceData.DeadLetter(key, leaseToken, deadLetteredAt);

    private static DurableOutputDeliveryLeaseRequest Request(
        DateTimeOffset now,
        string owner = "dead-letter-worker")
        => DurableOutputStoreConformanceData.DeliveryRequest(
            now,
            owner,
            TimeSpan.FromDays(1));
}
