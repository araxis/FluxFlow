using System.Diagnostics;
using FluxFlow.Engine.DurableOutput.Tests;
using Microsoft.Data.Sqlite;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.DurableOutput.SqlFile.Tests;

public sealed class SqlFileDurableOutputDeliveryInfrastructureTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 1, 11, 0, 0, TimeSpan.FromHours(2));

    [Fact]
    public async Task Capture_only_keeps_output_v1_and_first_lease_lazily_creates_delivery_v2()
    {
        using var database = TemporarySqliteDatabase.Create();
        var envelope = SqlFileDurableOutputTestData.CompleteValueEnvelope();
        await using var store = database.CreateStore();

        (await store.EnqueueAsync(envelope)).Status.ShouldBe(DurableOutputEnqueueStatus.Enqueued);
        await using (var captureOnly = await SqlFileDurableOutputTestDatabase.OpenAsync(
            database.DatabasePath))
        {
            (await ReadOwnedObjectsAsync(captureOnly)).ShouldBe([
                "fluxflow_durable_output_schema",
                "fluxflow_durable_outputs"
            ]);
            (await SqlFileDurableOutputTestDatabase.ScalarAsync<long>(
                captureOnly,
                "SELECT version FROM fluxflow_durable_output_schema WHERE singleton = 1;"))
                .ShouldBe(1);
        }

        var lease = await store.TryLeaseAsync(Request(Now));

        lease.ShouldNotBeNull();
        lease.Envelope.ShouldMatchExactly(envelope);
        await using var delivered = await SqlFileDurableOutputTestDatabase.OpenAsync(
            database.DatabasePath);
        (await ReadOwnedObjectsAsync(delivered)).ShouldBe([
            "fluxflow_durable_output_deliveries",
            "fluxflow_durable_output_delivery_schema",
            "fluxflow_durable_output_schema",
            "fluxflow_durable_outputs",
            "ix_fluxflow_durable_output_deliveries_dead_lettered",
            "ix_fluxflow_durable_output_deliveries_eligibility"
        ]);
        (await SqlFileDurableOutputTestDatabase.ScalarAsync<long>(
            delivered,
            "SELECT version FROM fluxflow_durable_output_schema WHERE singleton = 1;"))
            .ShouldBe(1);
        (await SqlFileDurableOutputTestDatabase.ScalarAsync<long>(
            delivered,
            "SELECT version FROM fluxflow_durable_output_delivery_schema WHERE singleton = 1;"))
            .ShouldBe(2);
        (await ReadColumnShapeAsync(delivered, "fluxflow_durable_output_deliveries"))
            .ShouldBe([
                "0|application_address|TEXT|1|1",
                "1|message_id|TEXT|1|2",
                "2|state|INTEGER|1|0",
                "3|next_attempt_utc_ticks|INTEGER|1|0",
                "4|next_attempt_offset_minutes|INTEGER|1|0",
                "5|lease_token|TEXT|0|0",
                "6|lease_owner|TEXT|0|0",
                "7|leased_at_utc_ticks|INTEGER|0|0",
                "8|leased_at_offset_minutes|INTEGER|0|0",
                "9|lease_until_utc_ticks|INTEGER|0|0",
                "10|lease_until_offset_minutes|INTEGER|0|0",
                "11|attempt|INTEGER|1|0",
                "12|delivered_at_utc_ticks|INTEGER|0|0",
                "13|delivered_at_offset_minutes|INTEGER|0|0",
                "14|dead_letter_reason|INTEGER|0|0",
                "15|dead_lettered_at_utc_ticks|INTEGER|0|0",
                "16|dead_lettered_at_offset_minutes|INTEGER|0|0",
                "17|dead_letter_generation|INTEGER|1|0"
            ]);
    }

    [Fact]
    public async Task Concurrent_sqlite_store_instances_persist_one_atomic_exclusive_lease()
    {
        using var database = TemporarySqliteDatabase.Create();
        var envelope = DurableOutputStoreConformanceData.Envelope("sqlite-one-winner");
        await using var first = database.CreateStore();
        await using var second = database.CreateStore();
        (await first.EnqueueAsync(envelope)).Status.ShouldBe(DurableOutputEnqueueStatus.Enqueued);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var operations = new[]
        {
            Task.Run(async () =>
            {
                await start.Task;
                return await first.TryLeaseAsync(Request(Now, "worker-a"));
            }),
            Task.Run(async () =>
            {
                await start.Task;
                return await second.TryLeaseAsync(Request(Now, "worker-b"));
            })
        };
        start.TrySetResult();
        var results = await Task.WhenAll(operations);

        results.Count(static lease => lease is not null).ShouldBe(1);
        results.Count(static lease => lease is null).ShouldBe(1);
        results.Single(static lease => lease is not null).ShouldNotBeNull()
            .Envelope.Key.ShouldBe(envelope.Key);
        await using var connection = await SqlFileDurableOutputTestDatabase.OpenAsync(
            database.DatabasePath);
        (await SqlFileDurableOutputTestDatabase.ScalarAsync<long>(
            connection,
            "SELECT COUNT(*) FROM fluxflow_durable_output_deliveries WHERE state = 2;"))
            .ShouldBe(1);
    }

    [Fact]
    public async Task Completed_tombstone_survives_reopen_with_exact_sqlite_state()
    {
        using var database = TemporarySqliteDatabase.Create();
        var envelope = DurableOutputStoreConformanceData.ErrorEnvelope("sqlite-tombstone");
        var completedAt = Now.AddSeconds(1);
        DurableOutputDeliveryLease lease;
        await using (var store = database.CreateStore())
        {
            await store.EnqueueAsync(envelope);
            lease = (await store.TryLeaseAsync(Request(Now))).ShouldNotBeNull();
            (await store.CompleteAsync(new DurableOutputDeliveryTransition(
                envelope.Key,
                lease.LeaseToken,
                completedAt))).Status
                .ShouldBe(DurableOutputDeliveryTransitionStatus.Applied);
        }

        await using var reopened = database.CreateStore(createDatabase: false, createDirectory: false);
        (await reopened.TryLeaseAsync(Request(Now.AddDays(1), "reopened"))).ShouldBeNull();
        await using var connection = await SqlFileDurableOutputTestDatabase.OpenAsync(
            database.DatabasePath);
        (await SqlFileDurableOutputTestDatabase.ReadStringsAsync(
            connection,
            """
            SELECT state || '|' || attempt || '|' ||
                   (lease_token IS NULL) || '|' || (lease_owner IS NULL) || '|' ||
                   delivered_at_utc_ticks || '|' || delivered_at_offset_minutes
            FROM fluxflow_durable_output_deliveries;
            """)).ShouldBe([
                $"3|1|1|1|{completedAt.UtcTicks}|{(int)completedAt.Offset.TotalMinutes}"
            ]);
    }

    [Fact]
    public async Task Successful_retry_reuses_one_sqlite_delivery_row()
    {
        using var database = TemporarySqliteDatabase.Create();
        var envelope = DurableOutputStoreConformanceData.Envelope("sqlite-retry-row");
        await using var store = database.CreateStore();
        await store.EnqueueAsync(envelope);
        var lease = (await store.TryLeaseAsync(Request(Now))).ShouldNotBeNull();
        var due = Now.AddMinutes(1);

        (await store.RetryAsync(new DurableOutputDeliveryRetry(
            envelope.Key,
            lease.LeaseToken,
            Now.AddSeconds(1),
            due))).Status.ShouldBe(DurableOutputDeliveryTransitionStatus.Applied);

        await using var connection = await SqlFileDurableOutputTestDatabase.OpenAsync(
            database.DatabasePath);
        (await SqlFileDurableOutputTestDatabase.ReadStringsAsync(
            connection,
            """
            SELECT state || '|' || next_attempt_utc_ticks || '|' ||
                   next_attempt_offset_minutes || '|' || attempt || '|' ||
                   (lease_token IS NULL) || '|' || (lease_owner IS NULL)
            FROM fluxflow_durable_output_deliveries;
            """)).ShouldBe([
                $"1|{due.UtcTicks}|{(int)due.Offset.TotalMinutes}|1|1|1"
            ]);
        (await SqlFileDurableOutputTestDatabase.ScalarAsync<long>(
            connection,
            "SELECT COUNT(*) FROM fluxflow_durable_outputs;"))
            .ShouldBe(1);
    }

    [Fact]
    public async Task Concurrent_sqlite_store_instances_lease_each_row_once_without_loss()
    {
        using var database = TemporarySqliteDatabase.Create();
        var envelopes = Enumerable.Range(0, 8)
            .Select(index => DurableOutputStoreConformanceData.Envelope($"sqlite-many-{index:D2}"))
            .ToArray();
        await using var writer = database.CreateStore();
        foreach (var envelope in envelopes)
            await writer.EnqueueAsync(envelope);
        var stores = Enumerable.Range(0, envelopes.Length)
            .Select(_ => database.CreateStore())
            .ToArray();
        try
        {
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var operations = stores.Select((store, index) => Task.Run(async () =>
            {
                await start.Task;
                return await store.TryLeaseAsync(Request(Now, $"worker-{index:D2}"));
            })).ToArray();
            start.TrySetResult();

            var leases = (await Task.WhenAll(operations))
                .Select(static lease => lease.ShouldNotBeNull())
                .ToArray();

            leases.Select(static lease => lease.Envelope.Key)
                .ShouldBe(envelopes.Select(static envelope => envelope.Key), ignoreOrder: true);
            leases.Select(static lease => lease.Envelope.Key).Distinct().Count()
                .ShouldBe(envelopes.Length);
            leases.Select(static lease => lease.LeaseToken).Distinct().Count()
                .ShouldBe(envelopes.Length);
            await using var connection = await SqlFileDurableOutputTestDatabase.OpenAsync(
                database.DatabasePath);
            (await SqlFileDurableOutputTestDatabase.ScalarAsync<long>(
                connection,
                "SELECT COUNT(*) FROM fluxflow_durable_output_deliveries WHERE state = 2;"))
                .ShouldBe(envelopes.Length);
        }
        finally
        {
            foreach (var store in stores)
                await store.DisposeAsync();
        }
    }

    [Fact]
    public async Task External_write_lock_bounds_delivery_lease_failure_and_store_recovers()
    {
        using var database = TemporarySqliteDatabase.Create();
        var timeout = TimeSpan.FromMilliseconds(150);
        var envelope = DurableOutputStoreConformanceData.Envelope("sqlite-lock");
        await using var store = database.CreateStore(busyTimeout: timeout);
        await store.EnqueueAsync(envelope);
        (await store.TryLeaseAsync(Request(envelope.CapturedAt.AddMinutes(-1), "initialize")))
            .ShouldBeNull();
        await using var lockConnection = await SqlFileDurableOutputTestDatabase.OpenAsync(
            database.DatabasePath);
        await using var writeLock = lockConnection.BeginTransaction(deferred: false);
        var stopwatch = Stopwatch.StartNew();

        var exception = await Should.ThrowAsync<InvalidOperationException>(() =>
            store.TryLeaseAsync(Request(Now, "blocked")).AsTask());
        stopwatch.Stop();

        exception.Message.ShouldContain("delivery lease");
        exception.Message.ShouldContain("configured busy timeout");
        exception.InnerException.ShouldBeOfType<SqliteException>();
        stopwatch.Elapsed.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(100));
        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5));
        await writeLock.RollbackAsync();
        var recovered = (await store.TryLeaseAsync(Request(Now, "recovered"))).ShouldNotBeNull();
        recovered.Envelope.Key.ShouldBe(envelope.Key);
        recovered.OwnerId.ShouldBe("recovered");
    }

    [Fact]
    public async Task Precancelled_first_delivery_does_not_create_database_and_disposal_rejects_delivery()
    {
        using var database = TemporarySqliteDatabase.Create();
        var nested = Path.Combine(database.DirectoryPath, "not-created");
        var path = Path.Combine(nested, "delivery.db");
        var store = new SqlFileDurableOutputStore(new SqlFileDurableOutputStoreOptions
        {
            DatabasePath = path,
            AllowAbsoluteDatabasePath = true
        });
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(() =>
            store.TryLeaseAsync(Request(Now), cancellation.Token).AsTask());

        Directory.Exists(nested).ShouldBeFalse();
        File.Exists(path).ShouldBeFalse();
        await store.DisposeAsync();
        await store.DisposeAsync();
        var exception = await Should.ThrowAsync<ObjectDisposedException>(() =>
            store.TryLeaseAsync(Request(Now)).AsTask());
        exception.ObjectName.ShouldContain(nameof(SqlFileDurableOutputStore));
        File.Exists(path).ShouldBeFalse();
    }

    private static DurableOutputDeliveryLeaseRequest Request(
        DateTimeOffset now,
        string owner = "worker-1")
        => new(owner, now, now.AddSeconds(30));

    private static async ValueTask<IReadOnlyList<string>> ReadOwnedObjectsAsync(
        SqliteConnection connection)
        => await SqlFileDurableOutputTestDatabase.ReadStringsAsync(
            connection,
            """
            SELECT name
            FROM sqlite_schema
            WHERE name LIKE 'fluxflow_durable_output%'
               OR name LIKE 'ix_fluxflow_durable_output%'
            ORDER BY name;
            """);

    private static async ValueTask<IReadOnlyList<string>> ReadColumnShapeAsync(
        SqliteConnection connection,
        string table)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info('{table}');";
        await using var reader = await command.ExecuteReaderAsync();
        var columns = new List<string>();
        while (await reader.ReadAsync())
        {
            columns.Add($"{reader.GetInt32(0)}|{reader.GetString(1)}|{reader.GetString(2)}|" +
                $"{reader.GetInt32(3)}|{reader.GetInt32(5)}");
        }

        return columns;
    }
}
