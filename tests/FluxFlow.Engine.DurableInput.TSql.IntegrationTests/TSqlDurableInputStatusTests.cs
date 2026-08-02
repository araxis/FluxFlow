using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.DurableInput.TSql.IntegrationTests;

public sealed class TSqlDurableInputStatusTests
{
    private static readonly DateTimeOffset ObservedAt = TSqlDurableInputTestSupport.Now;

    [Fact]
    public async Task A_second_store_observes_the_first_store_committed_status()
    {
        await using var database = await TSqlTestDatabase.CreateAsync();
        var writer = database.CreateStore();
        var observer = database.CreateStore();
        var envelope = TSqlDurableInputTestSupport.ValueEnvelope("status-multi-store");

        (await writer.EnqueueAsync(envelope)).Status.ShouldBe(DurableInputEnqueueStatus.Enqueued);

        var snapshot = await observer.GetStatusAsync(new(ObservedAt));

        snapshot.ShouldBe(new DurableInputStatusSnapshot(
            ObservedAt,
            pendingCount: 1,
            readyPendingCount: 1,
            leasedCount: 0,
            expiredLeaseCount: 0,
            deliveredCount: 0,
            deadLetteredCount: 0,
            oldestReadyAt: envelope.EnqueuedAt.ToUniversalTime(),
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
        (await database.ScalarAsync<long>(
            "SELECT COUNT_BIG(*) FROM sys.tables WHERE name LIKE N'fluxflow_relational_input%';"))
            .ShouldBe(0);
    }

    [Fact]
    public async Task Partial_schema_fails_without_adding_missing_columns_or_metadata()
    {
        await using var database = await TSqlTestDatabase.CreateAsync();
        await database.ExecuteAsync("CREATE TABLE dbo.fluxflow_relational_inputs (state tinyint NULL);");
        var store = database.CreateStore();

        var exception = await Should.ThrowAsync<SqlException>(() =>
            store.GetStatusAsync(new(ObservedAt)).AsTask());

        exception.Number.ShouldBe(207);
        (await database.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.fluxflow_relational_inputs');"))
            .ShouldBe(1);
        (await database.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.tables WHERE name = N'fluxflow_relational_input_schema';"))
            .ShouldBe(0);
    }

    [Fact]
    public async Task Malformed_schema_fails_without_renaming_or_repairing_the_column()
    {
        await using var database = await TSqlTestDatabase.CreateAsync();
        var store = database.CreateStore();
        (await store.EnqueueAsync(TSqlDurableInputTestSupport.ValueEnvelope("status-malformed-schema")))
            .Status.ShouldBe(DurableInputEnqueueStatus.Enqueued);
        await database.ExecuteAsync("""
            EXEC sys.sp_rename N'dbo.fluxflow_relational_inputs', N'fluxflow_relational_inputs_valid';
            CREATE TABLE dbo.fluxflow_relational_inputs (state tinyint NULL);
            """);

        var exception = await Should.ThrowAsync<SqlException>(() =>
            store.GetStatusAsync(new(ObservedAt)).AsTask());

        exception.Number.ShouldBe(207);
        (await database.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.tables WHERE name = N'fluxflow_relational_inputs_valid';"))
            .ShouldBe(1);
        (await database.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.fluxflow_relational_inputs');"))
            .ShouldBe(1);
    }

    [Fact]
    public async Task Status_ignores_corrupt_payload_columns_and_returns_exact_metadata()
    {
        await using var database = await TSqlTestDatabase.CreateAsync();
        var store = database.CreateStore();
        var envelope = TSqlDurableInputTestSupport.ValueEnvelope("status-corrupt-payload");
        (await store.EnqueueAsync(envelope)).Status.ShouldBe(DurableInputEnqueueStatus.Enqueued);
        await database.ExecuteAsync("""
            UPDATE dbo.fluxflow_relational_inputs
            SET payload_json = N'not-json', headers_json = N'also-not-json';
            """);

        var snapshot = await store.GetStatusAsync(new(ObservedAt));

        snapshot.ShouldBe(new DurableInputStatusSnapshot(
            ObservedAt,
            pendingCount: 1,
            readyPendingCount: 1,
            leasedCount: 0,
            expiredLeaseCount: 0,
            deliveredCount: 0,
            deadLetteredCount: 0,
            oldestReadyAt: envelope.EnqueuedAt.ToUniversalTime(),
            nextLeaseExpiry: null));
    }

    [Fact]
    public async Task Invalid_state_fails_visibly_without_repairing_the_row()
    {
        await using var database = await TSqlTestDatabase.CreateAsync();
        var store = database.CreateStore();
        (await store.EnqueueAsync(TSqlDurableInputTestSupport.ValueEnvelope("status-invalid-state")))
            .Status.ShouldBe(DurableInputEnqueueStatus.Enqueued);
        await database.ExecuteAsync("""
            ALTER TABLE dbo.fluxflow_relational_inputs NOCHECK CONSTRAINT ALL;
            UPDATE dbo.fluxflow_relational_inputs SET state = 99;
            """);

        var exception = await Should.ThrowAsync<InvalidDataException>(() =>
            store.GetStatusAsync(new(ObservedAt)).AsTask());

        exception.Message.ShouldBe("T-SQL durable input status found an invalid state value.");
        (await database.ScalarAsync<byte>("SELECT state FROM dbo.fluxflow_relational_inputs;"))
            .ShouldBe((byte)99);
    }

    [Fact]
    public async Task External_row_lock_times_out_then_status_recovers_with_the_committed_snapshot()
    {
        await using var database = await TSqlTestDatabase.CreateAsync();
        var store = database.CreateStore(new TSqlDurableInputStoreOptions
        {
            ConnectionString = database.ConnectionString,
            CommandTimeout = TimeSpan.FromSeconds(1)
        });
        var envelope = TSqlDurableInputTestSupport.ValueEnvelope("status-lock");
        (await store.EnqueueAsync(envelope)).Status.ShouldBe(DurableInputEnqueueStatus.Enqueued);

        await using (var lockConnection = await database.OpenConnectionAsync())
        await using (var transaction = await lockConnection.BeginTransactionAsync())
        {
            await using var command = lockConnection.CreateCommand();
            command.Transaction = (SqlTransaction)transaction;
            command.CommandText = "SELECT COUNT(*) FROM dbo.fluxflow_relational_inputs WITH (TABLOCKX, HOLDLOCK);";
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
        recovered.PendingCount.ShouldBe(1);
        recovered.ReadyPendingCount.ShouldBe(1);
        recovered.TotalCount.ShouldBe(1);
        recovered.OldestReadyAt.ShouldBe(envelope.EnqueuedAt.ToUniversalTime());
    }

    [Fact]
    public async Task Disposed_store_rejects_status_without_reopening_the_database()
    {
        await using var database = await TSqlTestDatabase.CreateAsync();
        var store = database.CreateStore();
        (await store.EnqueueAsync(TSqlDurableInputTestSupport.ValueEnvelope("status-disposed")))
            .Status.ShouldBe(DurableInputEnqueueStatus.Enqueued);
        await store.DisposeAsync();

        await Should.ThrowAsync<ObjectDisposedException>(() =>
            store.GetStatusAsync(new(ObservedAt)).AsTask());

        (await database.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.fluxflow_relational_inputs;"))
            .ShouldBe(1);
    }
}
