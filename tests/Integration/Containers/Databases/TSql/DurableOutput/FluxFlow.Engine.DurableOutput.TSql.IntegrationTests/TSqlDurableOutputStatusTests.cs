using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.DurableOutput.TSql.IntegrationTests;

public sealed class TSqlDurableOutputStatusTests
{
    private static readonly DateTimeOffset ObservedAt = TSqlDurableOutputTestSupport.Now;

    [Fact]
    public async Task A_second_store_observes_the_first_store_committed_unmaterialized_capture()
    {
        await using var database = await TSqlTestDatabase.CreateAsync();
        var writer = database.CreateStore();
        var observer = database.CreateStore();
        var envelope = TSqlDurableOutputTestSupport.ValueEnvelope(
            "status-multi-store",
            ObservedAt.AddMinutes(-1));

        (await writer.EnqueueAsync(envelope)).Status.ShouldBe(DurableOutputEnqueueStatus.Enqueued);

        var snapshot = await observer.GetStatusAsync(new(ObservedAt));

        snapshot.ShouldBe(new DurableOutputStatusSnapshot(
            ObservedAt,
            capturedCount: 1,
            unmaterializedCount: 1,
            readyUnmaterializedCount: 1,
            pendingCount: 0,
            readyPendingCount: 0,
            leasedCount: 0,
            expiredLeaseCount: 0,
            completedCount: 0,
            deadLetteredCount: 0,
            oldestReadyAt: envelope.CapturedAt.ToUniversalTime(),
            nextLeaseExpiry: null));
    }

    [Fact]
    public async Task Missing_schema_fails_without_creating_any_provider_table()
    {
        await using var database = await TSqlTestDatabase.CreateAsync();
        var store = database.CreateStore();

        var exception = await Should.ThrowAsync<SqlException>(() =>
            store.GetStatusAsync(new(ObservedAt)).AsTask());

        exception.Number.ShouldBe(208);
        (await TSqlDurableOutputTestSupport.ScalarAsync<long>(
            database,
            "SELECT COUNT_BIG(*) FROM sys.tables WHERE name LIKE N'fluxflow_relational_output%';"))
            .ShouldBe(0);
    }

    [Fact]
    public async Task Partial_schema_fails_without_adding_missing_columns_or_metadata()
    {
        await using var database = await TSqlTestDatabase.CreateAsync();
        await TSqlDurableOutputTestSupport.ExecuteAsync(database, """
            CREATE TABLE dbo.fluxflow_relational_outputs (captured_at_utc_ticks bigint NULL);
            CREATE TABLE dbo.fluxflow_relational_output_deliveries (state tinyint NULL);
            """);
        var store = database.CreateStore();

        var exception = await Should.ThrowAsync<SqlException>(() =>
            store.GetStatusAsync(new(ObservedAt)).AsTask());

        exception.Number.ShouldBe(207);
        (await TSqlDurableOutputTestSupport.ScalarAsync<int>(
            database,
            "SELECT COUNT(*) FROM sys.tables WHERE name LIKE N'fluxflow_relational_output%';"))
            .ShouldBe(2);
        (await TSqlDurableOutputTestSupport.ScalarAsync<int>(
            database,
            "SELECT COUNT(*) FROM sys.tables WHERE name = N'fluxflow_relational_output_schema';"))
            .ShouldBe(0);
    }

    [Fact]
    public async Task Malformed_schema_fails_without_renaming_or_repairing_the_column()
    {
        await using var database = await TSqlTestDatabase.CreateAsync();
        var store = database.CreateStore();
        (await store.EnqueueAsync(TSqlDurableOutputTestSupport.ValueEnvelope(
            "status-malformed-schema",
            ObservedAt.AddMinutes(-1))))
            .Status.ShouldBe(DurableOutputEnqueueStatus.Enqueued);
        await TSqlDurableOutputTestSupport.ExecuteAsync(
            database,
            """
            EXEC sys.sp_rename N'dbo.fluxflow_relational_outputs', N'fluxflow_relational_outputs_valid';
            CREATE TABLE dbo.fluxflow_relational_outputs (captured_at_utc_ticks bigint NULL);
            """);

        var exception = await Should.ThrowAsync<SqlException>(() =>
            store.GetStatusAsync(new(ObservedAt)).AsTask());

        exception.Number.ShouldBe(207);
        (await TSqlDurableOutputTestSupport.ScalarAsync<int>(
            database,
            "SELECT COUNT(*) FROM sys.tables WHERE name = N'fluxflow_relational_outputs_valid';"))
            .ShouldBe(1);
        (await TSqlDurableOutputTestSupport.ScalarAsync<int>(
            database,
            "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.fluxflow_relational_outputs');"))
            .ShouldBe(1);
    }

    [Fact]
    public async Task Status_ignores_corrupt_payload_columns_and_returns_exact_metadata()
    {
        await using var database = await TSqlTestDatabase.CreateAsync();
        var store = database.CreateStore();
        var envelope = TSqlDurableOutputTestSupport.ValueEnvelope(
            "status-corrupt-payload",
            ObservedAt.AddMinutes(-2));
        (await store.EnqueueAsync(envelope)).Status.ShouldBe(DurableOutputEnqueueStatus.Enqueued);
        await TSqlDurableOutputTestSupport.ExecuteAsync(database, """
            UPDATE dbo.fluxflow_relational_outputs
            SET payload_json = N'not-json', headers_json = N'also-not-json';
            """);

        var snapshot = await store.GetStatusAsync(new(ObservedAt));

        snapshot.ShouldBe(new DurableOutputStatusSnapshot(
            ObservedAt,
            capturedCount: 1,
            unmaterializedCount: 1,
            readyUnmaterializedCount: 1,
            pendingCount: 0,
            readyPendingCount: 0,
            leasedCount: 0,
            expiredLeaseCount: 0,
            completedCount: 0,
            deadLetteredCount: 0,
            oldestReadyAt: envelope.CapturedAt.ToUniversalTime(),
            nextLeaseExpiry: null));
    }

    [Fact]
    public async Task Invalid_delivery_state_fails_visibly_without_repairing_the_row()
    {
        await using var database = await TSqlTestDatabase.CreateAsync();
        var store = database.CreateStore();
        await TSqlDurableOutputTestSupport.CaptureAndLeaseAsync(
            store,
            TSqlDurableOutputTestSupport.ValueEnvelope(
                "status-invalid-state",
                ObservedAt.AddMinutes(-2)),
            ObservedAt.AddMinutes(-1));
        await TSqlDurableOutputTestSupport.ExecuteAsync(database, """
            ALTER TABLE dbo.fluxflow_relational_output_deliveries NOCHECK CONSTRAINT ALL;
            UPDATE dbo.fluxflow_relational_output_deliveries SET state = 99;
            """);

        var exception = await Should.ThrowAsync<InvalidDataException>(() =>
            store.GetStatusAsync(new(ObservedAt)).AsTask());

        exception.Message.ShouldBe("T-SQL durable output status found an invalid delivery state value.");
        (await TSqlDurableOutputTestSupport.ScalarAsync<byte>(
            database,
            "SELECT state FROM dbo.fluxflow_relational_output_deliveries;"))
            .ShouldBe((byte)99);
    }

    [Fact]
    public async Task Orphan_delivery_fails_visibly_without_deleting_or_materializing_rows()
    {
        await using var database = await TSqlTestDatabase.CreateAsync();
        var store = database.CreateStore();
        (await store.EnqueueAsync(TSqlDurableOutputTestSupport.ValueEnvelope(
            "status-schema-initializer",
            ObservedAt.AddMinutes(-2))))
            .Status.ShouldBe(DurableOutputEnqueueStatus.Enqueued);
        await TSqlDurableOutputTestSupport.ExecuteAsync(database, $"""
            ALTER TABLE dbo.fluxflow_relational_output_deliveries
                NOCHECK CONSTRAINT fk_fluxflow_relational_output_deliveries_output;
            INSERT INTO dbo.fluxflow_relational_output_deliveries (
                application_address,
                message_id,
                state,
                next_attempt_utc_ticks,
                next_attempt_offset_minutes,
                attempt,
                dead_letter_generation)
            VALUES (
                N'node:orphan/output:out',
                N'status-orphan',
                1,
                {ObservedAt.UtcTicks},
                0,
                0,
                0);
            """);

        var exception = await Should.ThrowAsync<InvalidDataException>(() =>
            store.GetStatusAsync(new(ObservedAt)).AsTask());

        exception.Message.ShouldBe("T-SQL durable output status found an orphan delivery row.");
        (await TSqlDurableOutputTestSupport.ScalarAsync<int>(
            database,
            "SELECT COUNT(*) FROM dbo.fluxflow_relational_output_deliveries WHERE message_id = N'status-orphan';"))
            .ShouldBe(1);
        (await TSqlDurableOutputTestSupport.ScalarAsync<int>(
            database,
            "SELECT COUNT(*) FROM dbo.fluxflow_relational_outputs;"))
            .ShouldBe(1);
    }

    [Fact]
    public async Task External_row_lock_times_out_then_status_recovers_with_the_committed_snapshot()
    {
        await using var database = await TSqlTestDatabase.CreateAsync();
        var store = database.CreateStore(new TSqlDurableOutputStoreOptions
        {
            ConnectionString = database.ConnectionString,
            CommandTimeout = TimeSpan.FromSeconds(1)
        });
        var envelope = TSqlDurableOutputTestSupport.ValueEnvelope(
            "status-lock",
            ObservedAt.AddMinutes(-1));
        (await store.EnqueueAsync(envelope)).Status.ShouldBe(DurableOutputEnqueueStatus.Enqueued);

        await using (var lockConnection = await database.OpenConnectionAsync())
        await using (var transaction = await lockConnection.BeginTransactionAsync())
        {
            await using var command = lockConnection.CreateCommand();
            command.Transaction = (SqlTransaction)transaction;
            command.CommandText = "SELECT COUNT(*) FROM dbo.fluxflow_relational_outputs WITH (TABLOCKX, HOLDLOCK);";
            (await command.ExecuteScalarAsync()).ShouldBe(1);

            var stopwatch = Stopwatch.StartNew();
            var exception = await Should.ThrowAsync<SqlException>(() =>
                store.GetStatusAsync(new(ObservedAt)).AsTask());
            stopwatch.Stop();

            exception.Number.ShouldBe(-2);
            stopwatch.Elapsed.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(800));
            stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(10));
            await transaction.RollbackAsync();
        }

        var recovered = await store.GetStatusAsync(new(ObservedAt));
        recovered.CapturedCount.ShouldBe(1);
        recovered.UnmaterializedCount.ShouldBe(1);
        recovered.ReadyUnmaterializedCount.ShouldBe(1);
        recovered.TrackedDeliveryCount.ShouldBe(0);
        recovered.OldestReadyAt.ShouldBe(envelope.CapturedAt.ToUniversalTime());
    }

    [Fact]
    public async Task Disposed_store_rejects_status_without_reopening_the_database()
    {
        await using var database = await TSqlTestDatabase.CreateAsync();
        var store = database.CreateStore();
        (await store.EnqueueAsync(TSqlDurableOutputTestSupport.ValueEnvelope(
            "status-disposed",
            ObservedAt.AddMinutes(-1))))
            .Status.ShouldBe(DurableOutputEnqueueStatus.Enqueued);
        await store.DisposeAsync();

        await Should.ThrowAsync<ObjectDisposedException>(() =>
            store.GetStatusAsync(new(ObservedAt)).AsTask());

        (await TSqlDurableOutputTestSupport.ScalarAsync<int>(
            database,
            "SELECT COUNT(*) FROM dbo.fluxflow_relational_outputs;"))
            .ShouldBe(1);
    }
}
