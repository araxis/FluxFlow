using FluxFlow.Engine.DurableInput.Tests;
using Microsoft.Data.Sqlite;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.DurableInput.SqlFile.Tests;

public sealed class SqlFileDurableInputRetentionTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Missing_terminal_timestamps_are_preserved_without_schema_change()
    {
        using var database = TemporarySqliteDatabase.Create();
        await using var store = database.CreateStore();
        var delivered = DurableInputStoreConformanceData.Envelope("missing-delivered-at");
        var deadLetter = DurableInputStoreConformanceData.Envelope("missing-dead-lettered-at");
        await DeliverAsync(store, delivered, Now.AddHours(-1));
        await DeadLetterAsync(store, deadLetter, Now.AddHours(-1));
        var schemaBefore = await ReadSchemaAsync(database.DatabasePath);
        await ClearTimestampAsync(
            database.DatabasePath,
            "delivered_at_utc_ticks",
            delivered.MessageId.Value);
        await ClearTimestampAsync(
            database.DatabasePath,
            "dead_lettered_at_utc_ticks",
            deadLetter.MessageId.Value);

        (await store.PurgeDeliveredAsync(new(Now))).DeletedCount.ShouldBe(0);
        (await store.PurgeDeadLettersAsync(new(Now))).DeletedCount.ShouldBe(0);

        (await ScalarAsync<long>(
            database.DatabasePath,
            "SELECT COUNT(*) FROM fluxflow_durable_inputs;")).ShouldBe(2);
        (await ScalarAsync<long>(
            database.DatabasePath,
            "SELECT version FROM fluxflow_durable_input_schema WHERE singleton = 1;"))
            .ShouldBe(2);
        (await ReadSchemaAsync(database.DatabasePath)).ShouldBe(schemaBefore);
    }

    [Fact]
    public async Task Retention_does_not_deserialize_payload_and_a_new_store_observes_commit()
    {
        using var database = TemporarySqliteDatabase.Create();
        var envelope = DurableInputStoreConformanceData.Envelope("corrupt-retention-payload");
        await using (var first = database.CreateStore())
        {
            await DeliverAsync(first, envelope, Now.AddHours(-1));
            await ExecuteAsync(
                database.DatabasePath,
                "UPDATE fluxflow_durable_inputs SET payload_json = 'not-json', headers_json = 'also-not-json';");

            (await first.PurgeDeliveredAsync(new(Now))).DeletedCount.ShouldBe(1);
        }

        await using var reopened = database.CreateStore(createDatabase: false);
        var status = await reopened.GetStatusAsync(new DurableInputStatusQuery(Now));
        status.TotalCount.ShouldBe(0);
        (await reopened.EnqueueAsync(envelope)).Status.ShouldBe(DurableInputEnqueueStatus.Enqueued);
    }

    [Fact]
    public async Task Locked_failed_batch_preserves_every_row_then_recovers_exactly()
    {
        using var database = TemporarySqliteDatabase.Create();
        await using var store = database.CreateStore(busyTimeout: TimeSpan.FromMilliseconds(25));
        await DeliverAsync(
            store,
            DurableInputStoreConformanceData.Envelope("locked-retention-1"),
            Now.AddHours(-2));
        await DeliverAsync(
            store,
            DurableInputStoreConformanceData.Envelope("locked-retention-2"),
            Now.AddHours(-1));
        await using var blocker = await OpenAsync(database.DatabasePath);
        await ExecuteAsync(blocker, "BEGIN IMMEDIATE;");

        var exception = await Should.ThrowAsync<InvalidOperationException>(() =>
            store.PurgeDeliveredAsync(new DurableInputRetentionRequest(Now)).AsTask());

        exception.Message.ShouldContain("delivered retention");
        (await ScalarAsync<long>(blocker, "SELECT COUNT(*) FROM fluxflow_durable_inputs;"))
            .ShouldBe(2);
        await ExecuteAsync(blocker, "ROLLBACK;");
        (await store.PurgeDeliveredAsync(new(Now))).DeletedCount.ShouldBe(2);
        (await ScalarAsync<long>(
            database.DatabasePath,
            "SELECT COUNT(*) FROM fluxflow_durable_inputs;")).ShouldBe(0);
    }

    private static async ValueTask DeliverAsync(
        SqlFileDurableInputStore store,
        DurableInputEnvelope envelope,
        DateTimeOffset deliveredAt)
    {
        (await store.EnqueueAsync(envelope)).Status.ShouldBe(DurableInputEnqueueStatus.Enqueued);
        var lease = (await store.LeaseAsync(DurableInputStoreConformanceData.Request(
            ownerId: $"retention-{envelope.MessageId.Value}",
            now: deliveredAt.AddMinutes(-1),
            leaseUntil: deliveredAt.AddHours(1),
            maxCount: 1))).ShouldHaveSingleItem();
        (await store.MarkDeliveredAsync(new(
            envelope.Key,
            lease.LeaseToken,
            deliveredAt))).Status.ShouldBe(DurableInputTransitionStatus.Applied);
    }

    private static async ValueTask DeadLetterAsync(
        SqlFileDurableInputStore store,
        DurableInputEnvelope envelope,
        DateTimeOffset deadLetteredAt)
    {
        (await store.EnqueueAsync(envelope)).Status.ShouldBe(DurableInputEnqueueStatus.Enqueued);
        var lease = (await store.LeaseAsync(DurableInputStoreConformanceData.Request(
            ownerId: $"retention-{envelope.MessageId.Value}",
            now: deadLetteredAt.AddMinutes(-1),
            leaseUntil: deadLetteredAt.AddHours(1),
            maxCount: 1))).ShouldHaveSingleItem();
        (await store.DeadLetterAsync(new(
            envelope.Key,
            lease.LeaseToken,
            deadLetteredAt,
            DurableInputStoreConformanceData.Failure()))).Status
            .ShouldBe(DurableInputTransitionStatus.Applied);
    }

    private static async ValueTask ClearTimestampAsync(
        string databasePath,
        string column,
        string messageId)
    {
        await using var connection = await OpenAsync(databasePath);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            PRAGMA ignore_check_constraints = ON;
            UPDATE fluxflow_durable_inputs
            SET {column} = NULL
            WHERE message_id = $messageId;
            """;
        command.Parameters.AddWithValue("$messageId", messageId);
        (await command.ExecuteNonQueryAsync()).ShouldBe(1);
    }

    private static async ValueTask<IReadOnlyList<string>> ReadSchemaAsync(string databasePath)
    {
        await using var connection = await OpenAsync(databasePath);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT type || ':' || name || ':' || COALESCE(sql, '')
            FROM sqlite_schema
            WHERE name LIKE 'fluxflow_durable_input%'
               OR name LIKE 'ix_fluxflow_durable_input%'
            ORDER BY type, name;
            """;
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<string>();
        while (await reader.ReadAsync())
            rows.Add(reader.GetString(0));
        return rows;
    }

    private static async ValueTask ExecuteAsync(string databasePath, string commandText)
    {
        await using var connection = await OpenAsync(databasePath);
        await ExecuteAsync(connection, commandText);
    }

    private static async ValueTask ExecuteAsync(SqliteConnection connection, string commandText)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync();
    }

    private static async ValueTask<T> ScalarAsync<T>(string databasePath, string commandText)
    {
        await using var connection = await OpenAsync(databasePath);
        return await ScalarAsync<T>(connection, commandText);
    }

    private static async ValueTask<T> ScalarAsync<T>(
        SqliteConnection connection,
        string commandText)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        return (T)(await command.ExecuteScalarAsync()).ShouldNotBeNull();
    }

    private static async ValueTask<SqliteConnection> OpenAsync(string databasePath)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false
        }.ToString());
        await connection.OpenAsync();
        return connection;
    }
}
