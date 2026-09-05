using System.Data;
using System.Text.Json;
using FluxFlow.Engine.DurableInput.Tests;
using Microsoft.Data.SqlClient;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.DurableInput.TSql.IntegrationTests;

public sealed class TSqlDurableInputConcurrencyTests
{
    [Fact]
    public async Task Equivalent_enqueue_across_stores_has_one_insert_and_only_equivalent_results()
    {
        await using var database = await TSqlTestDatabase.CreateAsync();
        await using var first = database.CreateStore();
        await using var second = database.CreateStore();
        var envelope = TSqlDurableInputTestSupport.ValueEnvelope("equivalent-race");

        var results = await RaceAsync(
            () => first.EnqueueAsync(envelope).AsTask(),
            () => second.EnqueueAsync(envelope).AsTask());

        results.Select(result => result.Status).ShouldBe([
            DurableInputEnqueueStatus.Enqueued,
            DurableInputEnqueueStatus.AlreadyExists
        ], ignoreOrder: true);
        (await database.ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.fluxflow_relational_inputs WHERE message_id = N'equivalent-race';"))
            .ShouldBe(1);
    }

    [Fact]
    public async Task Conflicting_enqueue_across_stores_keeps_one_complete_winner_without_overwrite()
    {
        await using var database = await TSqlTestDatabase.CreateAsync();
        await using var first = database.CreateStore();
        await using var second = database.CreateStore();
        var original = TSqlDurableInputTestSupport.ValueEnvelope("conflict-race");
        var conflicting = CopyWithPayload(
            original,
            JsonSerializer.SerializeToElement(new { different = true }));

        var results = await RaceAsync(
            () => first.EnqueueAsync(original).AsTask(),
            () => second.EnqueueAsync(conflicting).AsTask());

        results.Select(result => result.Status).ShouldBe([
            DurableInputEnqueueStatus.Enqueued,
            DurableInputEnqueueStatus.Conflict
        ], ignoreOrder: true);
        await using var reader = database.CreateStore();
        var stored = (await reader.LeaseAsync(new(
            "reader",
            original.EnqueuedAt,
            original.EnqueuedAt.AddMinutes(1),
            1))).ShouldHaveSingleItem().Envelope;
        new[] { original, conflicting }.Count(stored.HasSameContent).ShouldBe(1);
    }

    [Fact]
    public async Task Concurrent_multi_owner_batch_leases_are_disjoint_and_skipped_work_remains_available()
    {
        await using var database = await TSqlTestDatabase.CreateAsync();
        await using var writer = database.CreateStore();
        for (var index = 0; index < 10; index++)
        {
            (await writer.EnqueueAsync(
                DurableInputStoreConformanceData.Envelope($"batch-{index:D2}")))
                .Status.ShouldBe(DurableInputEnqueueStatus.Enqueued);
        }

        await using var first = database.CreateStore();
        await using var second = database.CreateStore();
        var results = await RaceAsync(
            () => first.LeaseAsync(new(
                "worker-a",
                DurableInputStoreConformanceData.Now,
                DurableInputStoreConformanceData.Now.AddMinutes(1),
                5)).AsTask(),
            () => second.LeaseAsync(new(
                "worker-b",
                DurableInputStoreConformanceData.Now,
                DurableInputStoreConformanceData.Now.AddMinutes(1),
                5)).AsTask());

        results[0].Count.ShouldBeLessThanOrEqualTo(5);
        results[1].Count.ShouldBeLessThanOrEqualTo(5);
        results[0].ShouldAllBe(lease => lease.OwnerId == "worker-a");
        results[1].ShouldAllBe(lease => lease.OwnerId == "worker-b");

        var concurrentLeases = results.SelectMany(leases => leases).ToArray();
        concurrentLeases.Length.ShouldBeInRange(5, 10);
        concurrentLeases.Select(lease => lease.Envelope.Key).Distinct().Count()
            .ShouldBe(concurrentLeases.Length);
        concurrentLeases.Select(lease => lease.LeaseToken).Distinct().Count()
            .ShouldBe(concurrentLeases.Length);

        await using var remainingReader = database.CreateStore();
        var remaining = await remainingReader.LeaseAsync(new(
            "worker-c",
            DurableInputStoreConformanceData.Now,
            DurableInputStoreConformanceData.Now.AddMinutes(1),
            10));
        remaining.ShouldAllBe(lease => lease.OwnerId == "worker-c");

        var allLeases = concurrentLeases.Concat(remaining).ToArray();
        allLeases.Length.ShouldBe(10);
        allLeases.Select(lease => lease.Envelope.Key).Distinct().Count().ShouldBe(10);
        allLeases.Select(lease => lease.LeaseToken).Distinct().Count().ShouldBe(10);
        (await database.ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.fluxflow_relational_inputs WHERE state = 1;"))
            .ShouldBe(10);
    }

    [Fact]
    public async Task Leasing_skips_row_locked_work_and_the_skipped_rows_remain_available()
    {
        await using var database = await TSqlTestDatabase.CreateAsync();
        await using var writer = database.CreateStore();
        for (var index = 0; index < 10; index++)
        {
            (await writer.EnqueueAsync(
                DurableInputStoreConformanceData.Envelope($"locked-batch-{index:D2}")))
                .Status.ShouldBe(DurableInputEnqueueStatus.Enqueued);
        }

        await using var blocker = await database.OpenConnectionAsync();
        await using var transaction = (SqlTransaction)await blocker.BeginTransactionAsync();
        await using (var command = blocker.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT message_id
                FROM dbo.fluxflow_relational_inputs
                    WITH (UPDLOCK, ROWLOCK, INDEX(ix_fluxflow_relational_inputs_eligibility))
                WHERE state = 0
                  AND next_attempt_utc_ticks <= @now
                  AND message_id < N'locked-batch-05'
                ORDER BY next_attempt_utc_ticks,
                         enqueued_at_utc_ticks,
                         application_address COLLATE Latin1_General_100_BIN2,
                         message_id COLLATE Latin1_General_100_BIN2;
                """;
            command.Parameters.Add("@now", SqlDbType.BigInt).Value =
                DurableInputStoreConformanceData.Now.UtcTicks;
            await using var reader = await command.ExecuteReaderAsync();
            var lockedCount = 0;
            while (await reader.ReadAsync())
                lockedCount++;
            lockedCount.ShouldBe(5);
        }

        await using var contender = database.CreateStore(new TSqlDurableInputStoreOptions
        {
            ConnectionString = database.ConnectionString,
            CommandTimeout = TimeSpan.FromSeconds(1)
        });
        var available = await contender.LeaseAsync(new(
            "available-worker",
            DurableInputStoreConformanceData.Now,
            DurableInputStoreConformanceData.Now.AddMinutes(1),
            5));

        available.Count.ShouldBe(5);
        available.ShouldAllBe(lease => lease.OwnerId == "available-worker");
        available.Select(lease => lease.Envelope.MessageId.Value).ShouldBe(
            Enumerable.Range(5, 5).Select(index => $"locked-batch-{index:D2}"),
            ignoreOrder: true);
        await transaction.RollbackAsync();

        await using var recovery = database.CreateStore();
        var recovered = await recovery.LeaseAsync(new(
            "recovery-worker",
            DurableInputStoreConformanceData.Now,
            DurableInputStoreConformanceData.Now.AddMinutes(1),
            5));
        recovered.Count.ShouldBe(5);
        recovered.ShouldAllBe(lease => lease.OwnerId == "recovery-worker");
        recovered.Select(lease => lease.Envelope.MessageId.Value).ShouldBe(
            Enumerable.Range(0, 5).Select(index => $"locked-batch-{index:D2}"),
            ignoreOrder: true);

        var allLeases = available.Concat(recovered).ToArray();
        allLeases.Select(lease => lease.Envelope.Key).Distinct().Count().ShouldBe(10);
        allLeases.Select(lease => lease.LeaseToken).Distinct().Count().ShouldBe(10);
        (await database.ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.fluxflow_relational_inputs WHERE state = 1;"))
            .ShouldBe(10);
    }

    [Fact]
    public async Task Concurrent_completion_has_one_applied_transition_and_one_terminal_row()
    {
        await using var database = await TSqlTestDatabase.CreateAsync();
        await using var initializer = database.CreateStore();
        var envelope = TSqlDurableInputTestSupport.ValueEnvelope("complete-race");
        var lease = await TSqlDurableInputTestSupport.EnqueueAndLeaseAsync(initializer, envelope);
        await using var first = database.CreateStore();
        await using var second = database.CreateStore();
        var transition = new DurableInputLeaseTransition(
            envelope.Key,
            lease.LeaseToken,
            TSqlDurableInputTestSupport.Now.AddMinutes(1));

        var results = await RaceAsync(
            () => first.MarkDeliveredAsync(transition).AsTask(),
            () => second.MarkDeliveredAsync(transition).AsTask());

        results.Select(result => result.Status).ShouldBe([
            DurableInputTransitionStatus.Applied,
            DurableInputTransitionStatus.InvalidState
        ], ignoreOrder: true);
        (await database.ScalarAsync<int>(
            "SELECT state FROM dbo.fluxflow_relational_inputs WHERE message_id = N'complete-race';"))
            .ShouldBe((int)DurableInputState.Delivered);
    }

    [Fact]
    public async Task Concurrent_release_has_one_applied_transition_and_one_exact_retry_schedule()
    {
        await using var database = await TSqlTestDatabase.CreateAsync();
        await using var initializer = database.CreateStore();
        var envelope = TSqlDurableInputTestSupport.ValueEnvelope("release-race");
        var lease = await TSqlDurableInputTestSupport.EnqueueAndLeaseAsync(initializer, envelope);
        await using var first = database.CreateStore();
        await using var second = database.CreateStore();
        var nextAttemptAt = TSqlDurableInputTestSupport.Now.AddMinutes(10);
        var release = new DurableInputRelease(
            envelope.Key,
            lease.LeaseToken,
            TSqlDurableInputTestSupport.Now.AddMinutes(1),
            nextAttemptAt,
            new(DurableInputFailureKind.InputUnavailable, "unavailable"));

        var results = await RaceAsync(
            () => first.ReleaseAsync(release).AsTask(),
            () => second.ReleaseAsync(release).AsTask());

        results.Select(result => result.Status).ShouldBe([
            DurableInputTransitionStatus.Applied,
            DurableInputTransitionStatus.InvalidState
        ], ignoreOrder: true);
        (await database.ScalarAsync<byte>(
            "SELECT state FROM dbo.fluxflow_relational_inputs WHERE message_id = N'release-race';"))
            .ShouldBe((byte)DurableInputState.Pending);
        (await database.ScalarAsync<long>(
            "SELECT next_attempt_utc_ticks FROM dbo.fluxflow_relational_inputs WHERE message_id = N'release-race';"))
            .ShouldBe(nextAttemptAt.UtcTicks);
    }

    [Fact]
    public async Task Concurrent_dead_letter_has_one_applied_transition_and_increments_generation_once()
    {
        await using var database = await TSqlTestDatabase.CreateAsync();
        await using var initializer = database.CreateStore();
        var envelope = TSqlDurableInputTestSupport.ValueEnvelope("dead-race");
        var lease = await TSqlDurableInputTestSupport.EnqueueAndLeaseAsync(initializer, envelope);
        await using var first = database.CreateStore();
        await using var second = database.CreateStore();
        var deadLetter = new DurableInputDeadLetter(
            envelope.Key,
            lease.LeaseToken,
            TSqlDurableInputTestSupport.Now.AddMinutes(1),
            new(DurableInputFailureKind.InvalidEnvelope, "invalid"));

        var results = await RaceAsync(
            () => first.DeadLetterAsync(deadLetter).AsTask(),
            () => second.DeadLetterAsync(deadLetter).AsTask());

        results.Select(result => result.Status).ShouldBe([
            DurableInputTransitionStatus.Applied,
            DurableInputTransitionStatus.InvalidState
        ], ignoreOrder: true);
        (await database.ScalarAsync<byte>(
            "SELECT state FROM dbo.fluxflow_relational_inputs WHERE message_id = N'dead-race';"))
            .ShouldBe((byte)DurableInputState.DeadLettered);
        (await database.ScalarAsync<long>(
            "SELECT dead_letter_generation FROM dbo.fluxflow_relational_inputs WHERE message_id = N'dead-race';"))
            .ShouldBe(1);
    }

    [Fact]
    public async Task Concurrent_renewal_and_completion_never_revive_or_overwrite_terminal_state()
    {
        await using var database = await TSqlTestDatabase.CreateAsync();
        await using var initializer = database.CreateStore();
        var envelope = TSqlDurableInputTestSupport.ValueEnvelope("renew-complete-race");
        var lease = await TSqlDurableInputTestSupport.EnqueueAndLeaseAsync(initializer, envelope);
        await using var first = database.CreateStore();
        await using var second = database.CreateStore();

        var results = await RaceAsync(
            () => first.RenewLeaseAsync(new(
                envelope.Key,
                lease.LeaseToken,
                TSqlDurableInputTestSupport.Now.AddSeconds(30),
                TSqlDurableInputTestSupport.Now.AddMinutes(10))).AsTask(),
            () => second.MarkDeliveredAsync(new(
                envelope.Key,
                lease.LeaseToken,
                TSqlDurableInputTestSupport.Now.AddMinutes(1))).AsTask());

        results[1].Status.ShouldBe(DurableInputTransitionStatus.Applied);
        results[0].Status.ShouldBeOneOf(
            DurableInputTransitionStatus.Applied,
            DurableInputTransitionStatus.InvalidState);
        (await database.ScalarAsync<byte>(
            "SELECT state FROM dbo.fluxflow_relational_inputs WHERE message_id = N'renew-complete-race';"))
            .ShouldBe((byte)DurableInputState.Delivered);
    }

    [Fact]
    public async Task Concurrent_renewal_and_release_never_overwrite_retry_state()
    {
        await using var database = await TSqlTestDatabase.CreateAsync();
        await using var initializer = database.CreateStore();
        var envelope = TSqlDurableInputTestSupport.ValueEnvelope("renew-release-race");
        var lease = await TSqlDurableInputTestSupport.EnqueueAndLeaseAsync(initializer, envelope);
        await using var first = database.CreateStore();
        await using var second = database.CreateStore();
        var nextAttemptAt = TSqlDurableInputTestSupport.Now.AddMinutes(10);

        var results = await RaceAsync(
            () => first.RenewLeaseAsync(new(
                envelope.Key,
                lease.LeaseToken,
                TSqlDurableInputTestSupport.Now.AddSeconds(30),
                TSqlDurableInputTestSupport.Now.AddMinutes(15))).AsTask(),
            () => second.ReleaseAsync(new(
                envelope.Key,
                lease.LeaseToken,
                TSqlDurableInputTestSupport.Now.AddMinutes(1),
                nextAttemptAt,
                new(DurableInputFailureKind.InputUnavailable, "unavailable"))).AsTask());

        results[1].Status.ShouldBe(DurableInputTransitionStatus.Applied);
        results[0].Status.ShouldBeOneOf(
            DurableInputTransitionStatus.Applied,
            DurableInputTransitionStatus.InvalidState);
        (await database.ScalarAsync<byte>(
            "SELECT state FROM dbo.fluxflow_relational_inputs WHERE message_id = N'renew-release-race';"))
            .ShouldBe((byte)DurableInputState.Pending);
        (await database.ScalarAsync<long>(
            "SELECT next_attempt_utc_ticks FROM dbo.fluxflow_relational_inputs WHERE message_id = N'renew-release-race';"))
            .ShouldBe(nextAttemptAt.UtcTicks);
    }

    [Fact]
    public async Task Concurrent_renewal_and_dead_letter_never_overwrite_terminal_state_or_failure()
    {
        await using var database = await TSqlTestDatabase.CreateAsync();
        await using var initializer = database.CreateStore();
        var envelope = TSqlDurableInputTestSupport.ValueEnvelope("renew-dead-race");
        var lease = await TSqlDurableInputTestSupport.EnqueueAndLeaseAsync(initializer, envelope);
        await using var first = database.CreateStore();
        await using var second = database.CreateStore();

        var results = await RaceAsync(
            () => first.RenewLeaseAsync(new(
                envelope.Key,
                lease.LeaseToken,
                TSqlDurableInputTestSupport.Now.AddSeconds(30),
                TSqlDurableInputTestSupport.Now.AddMinutes(15))).AsTask(),
            () => second.DeadLetterAsync(new(
                envelope.Key,
                lease.LeaseToken,
                TSqlDurableInputTestSupport.Now.AddMinutes(1),
                new(DurableInputFailureKind.InvalidEnvelope, "exact failure"))).AsTask());

        results[1].Status.ShouldBe(DurableInputTransitionStatus.Applied);
        results[0].Status.ShouldBeOneOf(
            DurableInputTransitionStatus.Applied,
            DurableInputTransitionStatus.InvalidState);
        (await database.ScalarAsync<byte>(
            "SELECT state FROM dbo.fluxflow_relational_inputs WHERE message_id = N'renew-dead-race';"))
            .ShouldBe((byte)DurableInputState.DeadLettered);
        (await database.ScalarAsync<string>(
            "SELECT failure_description FROM dbo.fluxflow_relational_inputs WHERE message_id = N'renew-dead-race';"))
            .ShouldBe("exact failure");
    }

    [Fact]
    public async Task Concurrent_replay_has_one_winner_and_advances_generation_once()
    {
        await using var database = await TSqlTestDatabase.CreateAsync();
        await using var initializer = database.CreateStore();
        var envelope = TSqlDurableInputTestSupport.ValueEnvelope("replay-race");
        var lease = await TSqlDurableInputTestSupport.EnqueueAndLeaseAsync(initializer, envelope);
        (await initializer.DeadLetterAsync(new(
            envelope.Key,
            lease.LeaseToken,
            TSqlDurableInputTestSupport.Now.AddMinutes(1),
            new(DurableInputFailureKind.InvalidEnvelope, "invalid"))))
            .Status.ShouldBe(DurableInputTransitionStatus.Applied);
        await using var first = database.CreateStore();
        await using var second = database.CreateStore();
        var replay = new DurableInputReplay(
            envelope.Key,
            expectedGeneration: 1,
            TSqlDurableInputTestSupport.Now.AddMinutes(2),
            TSqlDurableInputTestSupport.Now.AddMinutes(3));

        var results = await RaceAsync(
            () => first.ReplayAsync(replay).AsTask(),
            () => second.ReplayAsync(replay).AsTask());

        results.Count(result => result.Status == DurableInputReplayStatus.Replayed).ShouldBe(1);
        results.Count(result => result.Status is
            DurableInputReplayStatus.NotDeadLettered or DurableInputReplayStatus.GenerationMismatch)
            .ShouldBe(1);
        (await database.ScalarAsync<long>(
            "SELECT dead_letter_generation FROM dbo.fluxflow_relational_inputs WHERE message_id = N'replay-race';"))
            .ShouldBe(1);
        (await database.ScalarAsync<byte>(
            "SELECT state FROM dbo.fluxflow_relational_inputs WHERE message_id = N'replay-race';"))
            .ShouldBe((byte)DurableInputState.Pending);
    }

    private static async Task<T[]> RaceAsync<T>(Func<Task<T>> first, Func<Task<T>> second)
    {
        using var ready = new CountdownEvent(2);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<T> RunAsync(Func<Task<T>> operation)
        {
            ready.Signal();
            await start.Task;
            return await operation();
        }

        var firstTask = Task.Run(() => RunAsync(first));
        var secondTask = Task.Run(() => RunAsync(second));
        ready.Wait();
        start.SetResult();
        return await Task.WhenAll(firstTask, secondTask);
    }

    private static DurableInputEnvelope CopyWithPayload(
        DurableInputEnvelope source,
        JsonElement payload)
        => new(
            source.Address,
            source.ContractName,
            isError: false,
            payload,
            error: null,
            source.MessageId,
            source.TraceId,
            source.Timestamp,
            source.EnqueuedAt,
            source.CorrelationId,
            source.CausationId,
            source.Headers,
            source.SchemaVersion);
}
