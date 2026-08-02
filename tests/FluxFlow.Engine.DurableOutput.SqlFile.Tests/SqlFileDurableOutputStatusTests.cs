using FluxFlow.Engine.DurableOutput.Tests;
using Microsoft.Data.Sqlite;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.DurableOutput.SqlFile.Tests;

public sealed class SqlFileDurableOutputStatusTests
{
    private static readonly DateTimeOffset ObservedAt =
        new(2026, 8, 1, 16, 0, 0, TimeSpan.FromHours(-3));

    [Fact]
    public async Task Missing_database_status_creates_no_directory_or_file()
    {
        using var database = TemporarySqliteDatabase.Create();
        var directory = Path.Combine(database.DirectoryPath, "missing", "nested");
        var path = Path.Combine(directory, "status.db");
        await using var store = CreateStore(path);

        var exception = await Should.ThrowAsync<FileNotFoundException>(() =>
            store.GetStatusAsync(new(ObservedAt)).AsTask());

        exception.FileName.ShouldBe(path);
        Directory.Exists(directory).ShouldBeFalse();
        File.Exists(path).ShouldBeFalse();
    }

    [Fact]
    public async Task Capture_only_status_reports_ready_and_future_unmaterialized_without_creating_delivery_schema()
    {
        using var database = TemporarySqliteDatabase.Create();
        await using var store = database.CreateStore();
        await store.EnqueueAsync(DurableOutputStoreConformanceData.Envelope(
            "capture-ready",
            capturedAt: ObservedAt));
        await store.EnqueueAsync(DurableOutputStoreConformanceData.Envelope(
            "capture-future",
            capturedAt: ObservedAt.AddTicks(1)));

        var before = await ReadTableNamesAsync(database.DatabasePath);
        var snapshot = await store.GetStatusAsync(new(ObservedAt));
        var after = await ReadTableNamesAsync(database.DatabasePath);

        snapshot.CapturedCount.ShouldBe(2);
        snapshot.UnmaterializedCount.ShouldBe(2);
        snapshot.ReadyUnmaterializedCount.ShouldBe(1);
        snapshot.TrackedDeliveryCount.ShouldBe(0);
        snapshot.ReadyCount.ShouldBe(1);
        snapshot.OldestReadyAt.ShouldBe(ObservedAt);
        snapshot.NextLeaseExpiry.ShouldBeNull();
        after.ShouldBe(before);
        after.ShouldNotContain("fluxflow_durable_output_delivery_schema");
        after.ShouldNotContain("fluxflow_durable_output_deliveries");
    }

    [Fact]
    public async Task Delivery_initialization_and_backfill_change_the_next_snapshot_exactly()
    {
        using var database = TemporarySqliteDatabase.Create();
        await using var store = database.CreateStore();
        await store.EnqueueAsync(DurableOutputStoreConformanceData.Envelope(
            "status-backfill",
            capturedAt: ObservedAt.AddMinutes(-1)));
        var before = await store.GetStatusAsync(new(ObservedAt));

        var lease = (await store.TryLeaseAsync(new(
            "status-worker",
            ObservedAt,
            ObservedAt.AddMinutes(5)))).ShouldNotBeNull();
        var after = await store.GetStatusAsync(new(ObservedAt));

        before.UnmaterializedCount.ShouldBe(1);
        before.ReadyUnmaterializedCount.ShouldBe(1);
        before.TrackedDeliveryCount.ShouldBe(0);
        after.CapturedCount.ShouldBe(1);
        after.UnmaterializedCount.ShouldBe(0);
        after.LeasedCount.ShouldBe(1);
        after.ExpiredLeaseCount.ShouldBe(0);
        after.TrackedDeliveryCount.ShouldBe(1);
        after.ReadyCount.ShouldBe(0);
        after.NextLeaseExpiry.ShouldBe(lease.LeaseUntil);
    }

    [Fact]
    public async Task Status_ignores_corrupt_capture_payload_and_returns_exact_metadata()
    {
        using var database = TemporarySqliteDatabase.Create();
        await using var store = database.CreateStore();
        await store.EnqueueAsync(DurableOutputStoreConformanceData.Envelope(
            "status-corrupt-payload",
            capturedAt: ObservedAt.AddMinutes(-2)));
        await SqlFileDurableOutputTestDatabase.ExecuteAsync(
            database.DatabasePath,
            "UPDATE fluxflow_durable_outputs SET payload_json = 'not-json', headers_json = 'also-not-json';");

        var snapshot = await store.GetStatusAsync(new(ObservedAt));

        snapshot.CapturedCount.ShouldBe(1);
        snapshot.UnmaterializedCount.ShouldBe(1);
        snapshot.ReadyUnmaterializedCount.ShouldBe(1);
        snapshot.OldestReadyAt.ShouldBe(ObservedAt.AddMinutes(-2));
    }

    [Fact]
    public async Task Undefined_delivery_state_status_fails_visibly_without_mutation()
    {
        using var database = TemporarySqliteDatabase.Create();
        await using var store = database.CreateStore();
        await store.EnqueueAsync(DurableOutputStoreConformanceData.Envelope("status-invalid-state"));
        await store.TryLeaseAsync(DurableOutputStoreConformanceData.DeliveryRequest(ObservedAt));
        await SqlFileDurableOutputTestDatabase.ExecuteAsync(
            database.DatabasePath,
            "PRAGMA ignore_check_constraints = ON; UPDATE fluxflow_durable_output_deliveries SET state = 99;");

        var exception = await Should.ThrowAsync<InvalidDataException>(() =>
            store.GetStatusAsync(new(ObservedAt)).AsTask());
        await using var connection = await SqlFileDurableOutputTestDatabase.OpenAsync(database.DatabasePath);
        var state = await SqlFileDurableOutputTestDatabase.ScalarAsync<long>(
            connection,
            "SELECT state FROM fluxflow_durable_output_deliveries;");

        exception.Message.ShouldContain("invalid delivery state");
        state.ShouldBe(99);
    }

    [Fact]
    public async Task Orphan_delivery_state_status_fails_visibly_without_mutation()
    {
        using var database = TemporarySqliteDatabase.Create();
        await using var store = database.CreateStore();
        await store.TryLeaseAsync(DurableOutputStoreConformanceData.DeliveryRequest(ObservedAt));
        await SqlFileDurableOutputTestDatabase.ExecuteAsync(
            database.DatabasePath,
            $"""
            PRAGMA foreign_keys = OFF;
            INSERT INTO fluxflow_durable_output_deliveries (
                application_address, message_id, state,
                next_attempt_utc_ticks, next_attempt_offset_minutes,
                attempt, dead_letter_generation)
            VALUES ('workflow/Orphan/Port/Output', 'orphan', 1,
                    {ObservedAt.UtcTicks}, 0, 0, 0);
            """);

        var exception = await Should.ThrowAsync<InvalidDataException>(() =>
            store.GetStatusAsync(new(ObservedAt)).AsTask());
        await using var connection = await SqlFileDurableOutputTestDatabase.OpenAsync(database.DatabasePath);
        var rows = await SqlFileDurableOutputTestDatabase.ScalarAsync<long>(
            connection,
            "SELECT COUNT(*) FROM fluxflow_durable_output_deliveries;");

        exception.Message.ShouldContain("orphan delivery row");
        rows.ShouldBe(1);
    }

    [Fact]
    public async Task Busy_status_times_out_safely_then_recovers_with_the_exact_snapshot()
    {
        using var database = TemporarySqliteDatabase.Create();
        await using var store = database.CreateStore(busyTimeout: TimeSpan.FromMilliseconds(100));
        await store.EnqueueAsync(DurableOutputStoreConformanceData.Envelope(
            "status-busy",
            capturedAt: ObservedAt.AddMinutes(-1)));
        await using var lockConnection = await SqlFileDurableOutputTestDatabase.OpenAsync(
            database.DatabasePath);
        await ExecuteAsync(lockConnection, "BEGIN EXCLUSIVE;");

        var exception = await Should.ThrowAsync<InvalidOperationException>(() =>
            store.GetStatusAsync(new(ObservedAt)).AsTask());
        await ExecuteAsync(lockConnection, "ROLLBACK;");
        var recovered = await store.GetStatusAsync(new(ObservedAt));

        exception.Message.ShouldContain("status inspection");
        exception.Message.ShouldContain("configured busy timeout");
        recovered.CapturedCount.ShouldBe(1);
        recovered.UnmaterializedCount.ShouldBe(1);
        recovered.ReadyUnmaterializedCount.ShouldBe(1);
    }

    [Fact]
    public async Task Reopened_store_returns_the_same_snapshot_without_schema_mutation()
    {
        using var database = TemporarySqliteDatabase.Create();
        DurableOutputStatusSnapshot first;
        IReadOnlyList<string> schemaBefore;
        await using (var store = database.CreateStore())
        {
            await store.EnqueueAsync(DurableOutputStoreConformanceData.Envelope(
                "status-reopen",
                capturedAt: ObservedAt.AddMinutes(-3)));
            schemaBefore = await ReadSchemaAsync(database.DatabasePath);
            first = await store.GetStatusAsync(new(ObservedAt));
        }

        await using var reopened = database.CreateStore(createDatabase: false);
        var second = await reopened.GetStatusAsync(new(ObservedAt));
        var schemaAfter = await ReadSchemaAsync(database.DatabasePath);

        second.ShouldBe(first);
        schemaAfter.ShouldBe(schemaBefore);
    }

    [Fact]
    public async Task Disposed_store_rejects_status_without_reopening_the_database()
    {
        using var database = TemporarySqliteDatabase.Create();
        var store = database.CreateStore();
        await store.EnqueueAsync(DurableOutputStoreConformanceData.Envelope("status-disposed"));
        await store.DisposeAsync();

        var exception = await Should.ThrowAsync<ObjectDisposedException>(() =>
            store.GetStatusAsync(new(ObservedAt)).AsTask());

        exception.ObjectName.ShouldBe(typeof(SqlFileDurableOutputStore).FullName);
        File.Exists(database.DatabasePath).ShouldBeTrue();
    }

    private static SqlFileDurableOutputStore CreateStore(string path)
        => new(new SqlFileDurableOutputStoreOptions
        {
            DatabasePath = path,
            AllowAbsoluteDatabasePath = true,
            CreateDatabase = true,
            CreateDirectory = true
        });

    private static async ValueTask<IReadOnlyList<string>> ReadTableNamesAsync(string path)
    {
        await using var connection = await SqlFileDurableOutputTestDatabase.OpenAsync(path);
        return await SqlFileDurableOutputTestDatabase.ReadStringsAsync(
            connection,
            "SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name;");
    }

    private static async ValueTask<IReadOnlyList<string>> ReadSchemaAsync(string path)
    {
        await using var connection = await SqlFileDurableOutputTestDatabase.OpenAsync(path);
        return await SqlFileDurableOutputTestDatabase.ReadStringsAsync(
            connection,
            "SELECT type || ':' || name || ':' || COALESCE(sql, '') FROM sqlite_master ORDER BY type, name;");
    }

    private static async ValueTask ExecuteAsync(SqliteConnection connection, string commandText)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync();
    }
}
