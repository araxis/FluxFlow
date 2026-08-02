using System.Text.Json;
using FluxFlow.Engine.DurableInput.Tests;
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
    public async Task Concurrent_multi_owner_batch_leases_are_disjoint_and_cover_every_due_row()
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

        results[0].Count.ShouldBe(5);
        results[1].Count.ShouldBe(5);
        results[0].Select(lease => lease.Envelope.Key)
            .Intersect(results[1].Select(lease => lease.Envelope.Key))
            .ShouldBeEmpty();
        results.SelectMany(leases => leases)
            .Select(lease => lease.Envelope.Key)
            .Distinct()
            .Count().ShouldBe(10);
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
