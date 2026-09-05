using FluxFlow.Engine.DurableOutput.Tests;
using Microsoft.Data.Sqlite;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.DurableOutput.SqlFile.Tests;

public sealed class SqlFileDurableOutputRetentionTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Missing_terminal_timestamps_are_preserved_without_schema_change()
    {
        using var database = TemporarySqliteDatabase.Create();
        await using var store = database.CreateStore();
        var completed = DurableOutputStoreConformanceData.Envelope("missing-completed-at");
        var deadLetter = DurableOutputStoreConformanceData.Envelope("missing-dead-lettered-at");
        await CompleteAsync(store, completed, Now.AddHours(-1));
        await DeadLetterAsync(store, deadLetter, Now.AddHours(-1));
        var schemaBefore = await ReadSchemaAsync(database.DatabasePath);
        await ClearTimestampAsync(
            database.DatabasePath,
            "delivered_at_utc_ticks",
            completed.MessageId.Value);
        await ClearTimestampAsync(
            database.DatabasePath,
            "dead_lettered_at_utc_ticks",
            deadLetter.MessageId.Value);

        (await store.PurgeCompletedAsync(new(Now))).DeletedCount.ShouldBe(0);
        (await store.PurgeDeadLettersAsync(new(Now))).DeletedCount.ShouldBe(0);

        await using var connection = await SqlFileDurableOutputTestDatabase.OpenAsync(
            database.DatabasePath);
        (await SqlFileDurableOutputTestDatabase.ScalarAsync<long>(
            connection,
            "SELECT COUNT(*) FROM fluxflow_durable_outputs;")).ShouldBe(2);
        (await SqlFileDurableOutputTestDatabase.ScalarAsync<long>(
            connection,
            "SELECT COUNT(*) FROM fluxflow_durable_output_deliveries;")).ShouldBe(2);
        (await SqlFileDurableOutputTestDatabase.ScalarAsync<long>(
            connection,
            "SELECT version FROM fluxflow_durable_output_delivery_schema WHERE singleton = 1;"))
            .ShouldBe(2);
        (await ReadSchemaAsync(connection)).ShouldBe(schemaBefore);
    }

    [Fact]
    public async Task Parent_and_child_are_removed_atomically_without_payload_deserialization_and_reopen_observes_commit()
    {
        using var database = TemporarySqliteDatabase.Create();
        var completed = DurableOutputStoreConformanceData.Envelope("physical-completed");
        var deadLetter = DurableOutputStoreConformanceData.Envelope("physical-dead-letter");
        await using (var first = database.CreateStore())
        {
            await CompleteAsync(first, completed, Now.AddHours(-2));
            await DeadLetterAsync(first, deadLetter, Now.AddHours(-1));
            await SqlFileDurableOutputTestDatabase.ExecuteAsync(
                database.DatabasePath,
                "UPDATE fluxflow_durable_outputs SET payload_json = 'not-json', headers_json = 'also-not-json';");

            (await first.PurgeCompletedAsync(new(Now))).DeletedCount.ShouldBe(1);
            (await first.PurgeDeadLettersAsync(new(Now))).DeletedCount.ShouldBe(1);
        }

        await using (var verification = await SqlFileDurableOutputTestDatabase.OpenAsync(
            database.DatabasePath))
        {
            (await SqlFileDurableOutputTestDatabase.ScalarAsync<long>(
                verification,
                "SELECT COUNT(*) FROM fluxflow_durable_outputs;")).ShouldBe(0);
            (await SqlFileDurableOutputTestDatabase.ScalarAsync<long>(
                verification,
                "SELECT COUNT(*) FROM fluxflow_durable_output_deliveries;")).ShouldBe(0);
        }
        await using var reopened = database.CreateStore(createDatabase: false);
        var status = await reopened.GetStatusAsync(new DurableOutputStatusQuery(Now));
        status.CapturedCount.ShouldBe(0);
        status.TrackedDeliveryCount.ShouldBe(0);
        (await reopened.EnqueueAsync(completed)).Status.ShouldBe(DurableOutputEnqueueStatus.Enqueued);
        (await reopened.EnqueueAsync(deadLetter)).Status.ShouldBe(DurableOutputEnqueueStatus.Enqueued);
    }

    [Fact]
    public async Task Retention_first_use_initializes_only_existing_delivery_schema_and_preserves_capture_only_row()
    {
        using var database = TemporarySqliteDatabase.Create();
        await using var store = database.CreateStore();
        var captureOnly = DurableOutputStoreConformanceData.Envelope("retention-schema-capture-only");
        (await store.EnqueueAsync(captureOnly)).Status.ShouldBe(DurableOutputEnqueueStatus.Enqueued);
        await using (var before = await SqlFileDurableOutputTestDatabase.OpenAsync(
            database.DatabasePath))
        {
            (await SqlFileDurableOutputTestDatabase.ScalarAsync<long>(
                before,
                "SELECT COUNT(*) FROM sqlite_schema WHERE name = 'fluxflow_durable_output_delivery_schema';"))
                .ShouldBe(0);
        }

        (await store.PurgeCompletedAsync(new(Now.AddDays(1)))).DeletedCount.ShouldBe(0);

        await using var after = await SqlFileDurableOutputTestDatabase.OpenAsync(
            database.DatabasePath);
        (await SqlFileDurableOutputTestDatabase.ScalarAsync<long>(
            after,
            "SELECT version FROM fluxflow_durable_output_delivery_schema WHERE singleton = 1;"))
            .ShouldBe(2);
        (await SqlFileDurableOutputTestDatabase.ScalarAsync<long>(
            after,
            "SELECT COUNT(*) FROM fluxflow_durable_outputs;")).ShouldBe(1);
        (await SqlFileDurableOutputTestDatabase.ScalarAsync<long>(
            after,
            "SELECT COUNT(*) FROM fluxflow_durable_output_deliveries;")).ShouldBe(0);
        var ownedNames = await SqlFileDurableOutputTestDatabase.ReadStringsAsync(
            after,
            """
            SELECT name
            FROM sqlite_schema
            WHERE name LIKE 'fluxflow_durable_output%'
               OR name LIKE 'ix_fluxflow_durable_output%'
            ORDER BY name;
            """);
        ownedNames.ShouldContain("fluxflow_durable_output_delivery_schema");
        ownedNames.ShouldContain("fluxflow_durable_output_deliveries");
        ownedNames.ShouldNotContain(name => name.Contains("retention", StringComparison.OrdinalIgnoreCase));
        (await store.EnqueueAsync(captureOnly)).Status.ShouldBe(DurableOutputEnqueueStatus.AlreadyExists);
    }

    [Fact]
    public async Task Locked_failed_batch_preserves_every_parent_and_child_then_recovers_exactly()
    {
        using var database = TemporarySqliteDatabase.Create();
        await using var store = database.CreateStore(busyTimeout: TimeSpan.FromMilliseconds(25));
        await CompleteAsync(
            store,
            DurableOutputStoreConformanceData.Envelope("locked-retention-1"),
            Now.AddHours(-2));
        await CompleteAsync(
            store,
            DurableOutputStoreConformanceData.Envelope("locked-retention-2"),
            Now.AddHours(-1));
        await using var blocker = await SqlFileDurableOutputTestDatabase.OpenAsync(
            database.DatabasePath);
        await ExecuteAsync(blocker, "BEGIN IMMEDIATE;");

        var exception = await Should.ThrowAsync<InvalidOperationException>(() =>
            store.PurgeCompletedAsync(new DurableOutputRetentionRequest(Now)).AsTask());

        exception.Message.ShouldContain("completed retention");
        (await SqlFileDurableOutputTestDatabase.ScalarAsync<long>(
            blocker,
            "SELECT COUNT(*) FROM fluxflow_durable_outputs;")).ShouldBe(2);
        (await SqlFileDurableOutputTestDatabase.ScalarAsync<long>(
            blocker,
            "SELECT COUNT(*) FROM fluxflow_durable_output_deliveries;")).ShouldBe(2);
        await ExecuteAsync(blocker, "ROLLBACK;");
        (await store.PurgeCompletedAsync(new(Now))).DeletedCount.ShouldBe(2);
        await using var verification = await SqlFileDurableOutputTestDatabase.OpenAsync(
            database.DatabasePath);
        (await SqlFileDurableOutputTestDatabase.ScalarAsync<long>(
            verification,
            "SELECT COUNT(*) FROM fluxflow_durable_outputs;")).ShouldBe(0);
        (await SqlFileDurableOutputTestDatabase.ScalarAsync<long>(
            verification,
            "SELECT COUNT(*) FROM fluxflow_durable_output_deliveries;")).ShouldBe(0);
    }

    private static async ValueTask<DurableOutputDeliveryLease> EnqueueAndLeaseAsync(
        SqlFileDurableOutputStore store,
        DurableOutputEnvelope envelope,
        DateTimeOffset leaseAt)
    {
        (await store.EnqueueAsync(envelope)).Status.ShouldBe(DurableOutputEnqueueStatus.Enqueued);
        return (await store.TryLeaseAsync(DurableOutputStoreConformanceData.DeliveryRequest(
            leaseAt,
            ownerId: $"retention-{envelope.MessageId.Value}",
            leaseDuration: TimeSpan.FromHours(1)))).ShouldNotBeNull();
    }

    private static async ValueTask CompleteAsync(
        SqlFileDurableOutputStore store,
        DurableOutputEnvelope envelope,
        DateTimeOffset completedAt)
    {
        var lease = await EnqueueAndLeaseAsync(store, envelope, completedAt.AddMinutes(-1));
        lease.Envelope.Key.ShouldBe(envelope.Key);
        (await store.CompleteAsync(new(
            envelope.Key,
            lease.LeaseToken,
            completedAt))).Status.ShouldBe(DurableOutputDeliveryTransitionStatus.Applied);
    }

    private static async ValueTask DeadLetterAsync(
        SqlFileDurableOutputStore store,
        DurableOutputEnvelope envelope,
        DateTimeOffset deadLetteredAt)
    {
        var lease = await EnqueueAndLeaseAsync(store, envelope, deadLetteredAt.AddMinutes(-1));
        lease.Envelope.Key.ShouldBe(envelope.Key);
        (await store.DeadLetterAsync(DurableOutputStoreConformanceData.DeadLetter(
            envelope.Key,
            lease.LeaseToken,
            deadLetteredAt))).Status.ShouldBe(DurableOutputDeliveryTransitionStatus.Applied);
    }

    private static async ValueTask ClearTimestampAsync(
        string databasePath,
        string column,
        string messageId)
    {
        await using var connection = await SqlFileDurableOutputTestDatabase.OpenAsync(
            databasePath);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            PRAGMA ignore_check_constraints = ON;
            UPDATE fluxflow_durable_output_deliveries
            SET {column} = NULL
            WHERE message_id = $messageId;
            """;
        command.Parameters.AddWithValue("$messageId", messageId);
        (await command.ExecuteNonQueryAsync()).ShouldBe(1);
    }

    private static async ValueTask<IReadOnlyList<string>> ReadSchemaAsync(string databasePath)
    {
        await using var connection = await SqlFileDurableOutputTestDatabase.OpenAsync(
            databasePath);
        return await ReadSchemaAsync(connection);
    }

    private static ValueTask<IReadOnlyList<string>> ReadSchemaAsync(SqliteConnection connection)
        => SqlFileDurableOutputTestDatabase.ReadStringsAsync(
            connection,
            """
            SELECT type || ':' || name || ':' || COALESCE(sql, '')
            FROM sqlite_schema
            WHERE name LIKE 'fluxflow_durable_output%'
               OR name LIKE 'ix_fluxflow_durable_output%'
            ORDER BY type, name;
            """);

    private static async ValueTask ExecuteAsync(
        SqliteConnection connection,
        string commandText)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync();
    }
}
