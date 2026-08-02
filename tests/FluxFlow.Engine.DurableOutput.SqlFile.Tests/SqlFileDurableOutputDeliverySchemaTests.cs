using FluxFlow.Engine.DurableOutput.Tests;
using Microsoft.Data.Sqlite;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.DurableOutput.SqlFile.Tests;

public sealed class SqlFileDurableOutputDeliverySchemaTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Newer_delivery_schema_is_rejected_without_downgrade_or_mutation()
    {
        using var database = TemporarySqliteDatabase.Create();
        await CreateValidDeliverySchemaAsync(database);
        await SqlFileDurableOutputTestDatabase.ExecuteAsync(
            database.DatabasePath,
            "UPDATE fluxflow_durable_output_delivery_schema SET version = 3 WHERE singleton = 1;");
        await using var store = database.CreateStore();

        var exception = await Should.ThrowAsync<NotSupportedException>(() =>
            store.TryLeaseAsync(Request()).AsTask());

        exception.Message.ShouldContain("delivery schema version 3");
        exception.Message.ShouldContain("supported version 2");
        await using var connection = await SqlFileDurableOutputTestDatabase.OpenAsync(
            database.DatabasePath);
        (await SqlFileDurableOutputTestDatabase.ScalarAsync<long>(
            connection,
            "SELECT version FROM fluxflow_durable_output_delivery_schema WHERE singleton = 1;"))
            .ShouldBe(3);
    }

    [Fact]
    public async Task Unversioned_delivery_table_is_rejected_without_adoption()
    {
        using var database = TemporarySqliteDatabase.Create();
        await using (var capture = database.CreateStore())
            await capture.EnqueueAsync(DurableOutputStoreConformanceData.Envelope());
        await SqlFileDurableOutputTestDatabase.ExecuteAsync(
            database.DatabasePath,
            "CREATE TABLE fluxflow_durable_output_deliveries (foreign_value TEXT NOT NULL);");
        await using var store = database.CreateStore();

        var exception = await Should.ThrowAsync<InvalidDataException>(() =>
            store.TryLeaseAsync(Request()).AsTask());

        exception.Message.ShouldContain("unversioned delivery table");
        await using var connection = await SqlFileDurableOutputTestDatabase.OpenAsync(
            database.DatabasePath);
        (await SqlFileDurableOutputTestDatabase.ReadStringsAsync(
            connection,
            "SELECT name FROM pragma_table_info('fluxflow_durable_output_deliveries');"))
            .ShouldBe(["foreign_value"]);
        (await SqlFileDurableOutputTestDatabase.ScalarAsync<long>(
            connection,
            "SELECT COUNT(*) FROM sqlite_schema WHERE name = 'fluxflow_durable_output_delivery_schema';"))
            .ShouldBe(0);
    }

    [Fact]
    public async Task Missing_delivery_version_row_is_rejected_as_corrupt_metadata()
    {
        using var database = TemporarySqliteDatabase.Create();
        await CreateValidDeliverySchemaAsync(database);
        await SqlFileDurableOutputTestDatabase.ExecuteAsync(
            database.DatabasePath,
            "DELETE FROM fluxflow_durable_output_delivery_schema;");
        await using var store = database.CreateStore();

        var exception = await Should.ThrowAsync<InvalidDataException>(() =>
            store.TryLeaseAsync(Request()).AsTask());

        exception.Message.ShouldContain("delivery schema version is missing");
        await using var connection = await SqlFileDurableOutputTestDatabase.OpenAsync(
            database.DatabasePath);
        (await SqlFileDurableOutputTestDatabase.ScalarAsync<long>(
            connection,
            "SELECT COUNT(*) FROM fluxflow_durable_output_delivery_schema;"))
            .ShouldBe(0);
    }

    [Fact]
    public async Task Fresh_delivery_schema_is_exact_version_two_with_dead_letter_checks_and_indexes()
    {
        using var database = TemporarySqliteDatabase.Create();
        await CreateValidDeliverySchemaAsync(database);
        await using var connection = await SqlFileDurableOutputTestDatabase.OpenAsync(
            database.DatabasePath);

        (await SqlFileDurableOutputTestDatabase.ScalarAsync<long>(
            connection,
            "SELECT version FROM fluxflow_durable_output_delivery_schema WHERE singleton = 1;"))
            .ShouldBe(2);
        (await SqlFileDurableOutputTestDatabase.ScalarAsync<long>(
            connection,
            "SELECT COUNT(*) FROM pragma_table_info('fluxflow_durable_output_deliveries');"))
            .ShouldBe(18);
        var definitions = await SqlFileDurableOutputTestDatabase.ReadStringsAsync(
            connection,
            """
            SELECT sql
            FROM sqlite_schema
            WHERE name IN (
                'fluxflow_durable_output_deliveries',
                'ix_fluxflow_durable_output_deliveries_dead_lettered')
            ORDER BY name;
            """);
        definitions.Count.ShouldBe(2);
        var table = definitions.Single(definition => definition.Contains(
            "CREATE TABLE fluxflow_durable_output_deliveries", StringComparison.Ordinal));
        table.ShouldContain("state IN (1, 2, 3, 4)");
        table.ShouldContain("dead_letter_reason");
        table.ShouldContain("dead_letter_reason = 1");
        table.ShouldContain("dead_letter_generation INTEGER NOT NULL DEFAULT 0");
        table.ShouldContain("state = 4");
        var index = definitions.Single(definition => definition.Contains(
            "ix_fluxflow_durable_output_deliveries_dead_lettered", StringComparison.Ordinal));
        index.ShouldContain("dead_lettered_at_utc_ticks DESC");
        index.ShouldContain("application_address");
        index.ShouldContain("message_id");
        index.ShouldContain("WHERE state = 4");
    }

    [Theory]
    [InlineData(
        "DROP TABLE fluxflow_durable_output_deliveries;",
        "missing required table 'fluxflow_durable_output_deliveries'")]
    [InlineData(
        "DROP INDEX ix_fluxflow_durable_output_deliveries_eligibility;",
        "missing required index 'ix_fluxflow_durable_output_deliveries_eligibility'")]
    [InlineData(
        "DROP INDEX ix_fluxflow_durable_output_deliveries_dead_lettered;",
        "missing required index 'ix_fluxflow_durable_output_deliveries_dead_lettered'")]
    public async Task Missing_required_delivery_artifact_is_rejected_without_repair(
        string mutation,
        string expectedMessage)
    {
        using var database = TemporarySqliteDatabase.Create();
        await CreateValidDeliverySchemaAsync(database);
        await SqlFileDurableOutputTestDatabase.ExecuteAsync(database.DatabasePath, mutation);
        await using var store = database.CreateStore();

        var exception = await Should.ThrowAsync<InvalidDataException>(() =>
            store.TryLeaseAsync(Request()).AsTask());

        exception.Message.ShouldContain(expectedMessage);
        await using var connection = await SqlFileDurableOutputTestDatabase.OpenAsync(
            database.DatabasePath);
        var missingName = mutation.Contains("TABLE", StringComparison.Ordinal)
            ? "fluxflow_durable_output_deliveries"
            : mutation.Contains("dead_lettered", StringComparison.Ordinal)
                ? "ix_fluxflow_durable_output_deliveries_dead_lettered"
                : "ix_fluxflow_durable_output_deliveries_eligibility";
        (await SqlFileDurableOutputTestDatabase.ScalarAsync<long>(
            connection,
            $"SELECT COUNT(*) FROM sqlite_schema WHERE name = '{missingName}';"))
            .ShouldBe(0);
    }

    [Fact]
    public async Task Incompatible_delivery_table_is_rejected_without_destructive_repair()
    {
        using var database = TemporarySqliteDatabase.Create();
        await CreateValidDeliverySchemaAsync(database);
        await SqlFileDurableOutputTestDatabase.ExecuteAsync(
            database.DatabasePath,
            """
            DROP TABLE fluxflow_durable_output_deliveries;
            CREATE TABLE fluxflow_durable_output_deliveries (foreign_value TEXT NOT NULL);
            """);
        await using var store = database.CreateStore();

        var exception = await Should.ThrowAsync<InvalidDataException>(() =>
            store.TryLeaseAsync(Request()).AsTask());

        exception.Message.ShouldContain("incompatible column count");
        await using var connection = await SqlFileDurableOutputTestDatabase.OpenAsync(
            database.DatabasePath);
        (await SqlFileDurableOutputTestDatabase.ReadStringsAsync(
            connection,
            "SELECT name FROM pragma_table_info('fluxflow_durable_output_deliveries');"))
            .ShouldBe(["foreign_value"]);
    }

    [Fact]
    public async Task Corrupt_delivery_state_row_is_rejected_without_rewrite()
    {
        using var database = TemporarySqliteDatabase.Create();
        await CreateValidDeliverySchemaAsync(database);
        await SqlFileDurableOutputTestDatabase.ExecuteAsync(
            database.DatabasePath,
            """
            PRAGMA ignore_check_constraints = ON;
            UPDATE fluxflow_durable_output_deliveries SET state = 99;
            """);
        await using var store = database.CreateStore();

        var exception = await Should.ThrowAsync<InvalidDataException>(() =>
            store.TryLeaseAsync(Request()).AsTask());

        exception.Message.ShouldContain("corrupt state rows");
        await using var connection = await SqlFileDurableOutputTestDatabase.OpenAsync(
            database.DatabasePath);
        (await SqlFileDurableOutputTestDatabase.ScalarAsync<long>(
            connection,
            "SELECT state FROM fluxflow_durable_output_deliveries;"))
            .ShouldBe(99);
    }

    [Fact]
    public async Task Corrupt_sqlite_file_is_rejected_without_replacement()
    {
        using var database = TemporarySqliteDatabase.Create();
        var bytes = "not a sqlite database"u8.ToArray();
        await File.WriteAllBytesAsync(database.DatabasePath, bytes);
        await using var store = database.CreateStore(createDatabase: false);

        var exception = await Should.ThrowAsync<SqliteException>(() =>
            store.TryLeaseAsync(Request()).AsTask());

        exception.SqliteErrorCode.ShouldBe(26);
        await store.DisposeAsync();
        (await File.ReadAllBytesAsync(database.DatabasePath)).ShouldBe(bytes);
    }

    [Fact]
    public async Task Version_one_migration_preserves_pending_row_exactly_and_initializes_dead_letter_fields()
    {
        using var database = TemporarySqliteDatabase.Create();
        var envelope = SqlFileDurableOutputTestData.CompleteValueEnvelope("migrate-pending");
        await CreateVersionOneDatabaseAsync(database, envelope, state: 1);
        await using var store = database.CreateStore(createDatabase: false, createDirectory: false);

        (await store.GetAsync(envelope.Key)).ShouldBeNull();

        await AssertMigratedVersionAndStateAsync(
            database,
            expectedState: 1,
            expectedAttempt: 0,
            expectedLeaseToken: null,
            expectedOwner: null,
            expectedDeliveredAtUtcTicks: null);
    }

    [Fact]
    public async Task Version_one_migration_preserves_leased_row_token_attempt_times_and_offsets()
    {
        using var database = TemporarySqliteDatabase.Create();
        var envelope = SqlFileDurableOutputTestData.CompleteValueEnvelope("migrate-leased");
        await CreateVersionOneDatabaseAsync(database, envelope, state: 2);
        await using var store = database.CreateStore(createDatabase: false, createDirectory: false);

        (await store.GetAsync(envelope.Key)).ShouldBeNull();

        await AssertMigratedVersionAndStateAsync(
            database,
            expectedState: 2,
            expectedAttempt: 2,
            expectedLeaseToken: MigrationLeaseToken.ToString("N"),
            expectedOwner: "migration-worker",
            expectedDeliveredAtUtcTicks: null);
        await using var connection = await SqlFileDurableOutputTestDatabase.OpenAsync(
            database.DatabasePath);
        (await SqlFileDurableOutputTestDatabase.ReadStringsAsync(
            connection,
            """
            SELECT leased_at_utc_ticks || '|' || leased_at_offset_minutes || '|' ||
                   lease_until_utc_ticks || '|' || lease_until_offset_minutes
            FROM fluxflow_durable_output_deliveries;
            """)).ShouldBe([$"{Now.UtcTicks}|{(int)Now.Offset.TotalMinutes}|" +
                $"{Now.AddMinutes(10).UtcTicks}|{(int)Now.Offset.TotalMinutes}"]);
    }

    [Fact]
    public async Task Version_one_migration_preserves_completed_tombstone_and_offsets()
    {
        using var database = TemporarySqliteDatabase.Create();
        var envelope = SqlFileDurableOutputTestData.CompleteValueEnvelope("migrate-completed");
        await CreateVersionOneDatabaseAsync(database, envelope, state: 3);
        await using var store = database.CreateStore(createDatabase: false, createDirectory: false);

        (await store.GetAsync(envelope.Key)).ShouldBeNull();

        await AssertMigratedVersionAndStateAsync(
            database,
            expectedState: 3,
            expectedAttempt: 2,
            expectedLeaseToken: null,
            expectedOwner: null,
            expectedDeliveredAtUtcTicks: Now.AddMinutes(15).UtcTicks);
        await using var connection = await SqlFileDurableOutputTestDatabase.OpenAsync(
            database.DatabasePath);
        (await SqlFileDurableOutputTestDatabase.ScalarAsync<long>(
            connection,
            "SELECT delivered_at_offset_minutes FROM fluxflow_durable_output_deliveries;"))
            .ShouldBe((long)Now.Offset.TotalMinutes);
    }

    [Fact]
    public async Task Cancelled_version_one_migration_leaves_complete_original_schema()
    {
        using var database = TemporarySqliteDatabase.Create();
        var envelope = SqlFileDurableOutputTestData.CompleteValueEnvelope("migrate-cancelled");
        await CreateVersionOneDatabaseAsync(database, envelope, state: 1);
        await using var store = database.CreateStore(createDatabase: false, createDirectory: false);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(() =>
            store.GetAsync(envelope.Key, cancellation.Token).AsTask());

        await using var connection = await SqlFileDurableOutputTestDatabase.OpenAsync(
            database.DatabasePath);
        (await SqlFileDurableOutputTestDatabase.ScalarAsync<long>(
            connection,
            "SELECT version FROM fluxflow_durable_output_delivery_schema WHERE singleton = 1;"))
            .ShouldBe(1);
        (await SqlFileDurableOutputTestDatabase.ScalarAsync<long>(
            connection,
            "SELECT COUNT(*) FROM pragma_table_info('fluxflow_durable_output_deliveries');"))
            .ShouldBe(14);
        (await SqlFileDurableOutputTestDatabase.ScalarAsync<long>(
            connection,
            "SELECT COUNT(*) FROM sqlite_schema WHERE name = 'fluxflow_durable_output_deliveries_v2';"))
            .ShouldBe(0);
    }

    [Fact]
    public async Task Partially_upgraded_delivery_schema_is_rejected_without_repair()
    {
        using var database = TemporarySqliteDatabase.Create();
        var envelope = SqlFileDurableOutputTestData.CompleteValueEnvelope("migrate-partial");
        await CreateVersionOneDatabaseAsync(database, envelope, state: 1);
        await SqlFileDurableOutputTestDatabase.ExecuteAsync(
            database.DatabasePath,
            "CREATE TABLE fluxflow_durable_output_deliveries_v2 (foreign_value TEXT NOT NULL);");
        await using var store = database.CreateStore(createDatabase: false, createDirectory: false);

        var exception = await Should.ThrowAsync<InvalidDataException>(() =>
            store.GetAsync(envelope.Key).AsTask());

        exception.Message.ShouldContain("partial version-2 objects");
        await using var connection = await SqlFileDurableOutputTestDatabase.OpenAsync(
            database.DatabasePath);
        (await SqlFileDurableOutputTestDatabase.ScalarAsync<long>(
            connection,
            "SELECT version FROM fluxflow_durable_output_delivery_schema WHERE singleton = 1;"))
            .ShouldBe(1);
        (await SqlFileDurableOutputTestDatabase.ReadStringsAsync(
            connection,
            "SELECT name FROM pragma_table_info('fluxflow_durable_output_deliveries_v2');"))
            .ShouldBe(["foreign_value"]);
    }

    [Fact]
    public async Task Corrupt_version_one_row_rolls_back_without_migration()
    {
        using var database = TemporarySqliteDatabase.Create();
        var envelope = SqlFileDurableOutputTestData.CompleteValueEnvelope("migrate-corrupt");
        await CreateVersionOneDatabaseAsync(database, envelope, state: 99);
        await using var store = database.CreateStore(createDatabase: false, createDirectory: false);

        var exception = await Should.ThrowAsync<InvalidDataException>(() =>
            store.GetAsync(envelope.Key).AsTask());

        exception.Message.ShouldContain("corrupt state rows");
        await using var connection = await SqlFileDurableOutputTestDatabase.OpenAsync(
            database.DatabasePath);
        (await SqlFileDurableOutputTestDatabase.ScalarAsync<long>(
            connection,
            "SELECT version FROM fluxflow_durable_output_delivery_schema WHERE singleton = 1;"))
            .ShouldBe(1);
        (await SqlFileDurableOutputTestDatabase.ScalarAsync<long>(
            connection,
            "SELECT state FROM fluxflow_durable_output_deliveries;"))
            .ShouldBe(99);
        (await SqlFileDurableOutputTestDatabase.ScalarAsync<long>(
            connection,
            "SELECT COUNT(*) FROM pragma_table_info('fluxflow_durable_output_deliveries');"))
            .ShouldBe(14);
    }

    private static readonly Guid MigrationLeaseToken =
        Guid.Parse("805a82d5-f9d5-44cd-ab71-324219c89fd4");

    private static async ValueTask CreateVersionOneDatabaseAsync(
        TemporarySqliteDatabase database,
        DurableOutputEnvelope envelope,
        int state)
    {
        await using (var capture = database.CreateStore())
            (await capture.EnqueueAsync(envelope)).Status.ShouldBe(DurableOutputEnqueueStatus.Enqueued);

        var isLeased = state == 2;
        var isCompleted = state == 3;
        var attempt = state == 1 ? 0 : 2;
        static string SqlText(string? value) => value is null ? "NULL" : $"'{value}'";
        static string SqlNumber(long? value) => value?.ToString() ?? "NULL";
        await SqlFileDurableOutputTestDatabase.ExecuteAsync(
            database.DatabasePath,
            $"""
            CREATE TABLE fluxflow_durable_output_delivery_schema (
                singleton INTEGER NOT NULL PRIMARY KEY,
                version INTEGER NOT NULL
            ) WITHOUT ROWID;
            INSERT INTO fluxflow_durable_output_delivery_schema (singleton, version)
            VALUES (1, 1);
            CREATE TABLE fluxflow_durable_output_deliveries (
                application_address TEXT NOT NULL,
                message_id TEXT NOT NULL,
                state INTEGER NOT NULL,
                next_attempt_utc_ticks INTEGER NOT NULL,
                next_attempt_offset_minutes INTEGER NOT NULL,
                lease_token TEXT NULL,
                lease_owner TEXT NULL,
                leased_at_utc_ticks INTEGER NULL,
                leased_at_offset_minutes INTEGER NULL,
                lease_until_utc_ticks INTEGER NULL,
                lease_until_offset_minutes INTEGER NULL,
                attempt INTEGER NOT NULL,
                delivered_at_utc_ticks INTEGER NULL,
                delivered_at_offset_minutes INTEGER NULL,
                PRIMARY KEY (application_address, message_id)
            ) WITHOUT ROWID;
            CREATE INDEX ix_fluxflow_durable_output_deliveries_eligibility
                ON fluxflow_durable_output_deliveries (
                    state,
                    next_attempt_utc_ticks,
                    application_address,
                    message_id);
            INSERT INTO fluxflow_durable_output_deliveries (
                application_address, message_id, state,
                next_attempt_utc_ticks, next_attempt_offset_minutes,
                lease_token, lease_owner,
                leased_at_utc_ticks, leased_at_offset_minutes,
                lease_until_utc_ticks, lease_until_offset_minutes,
                attempt, delivered_at_utc_ticks, delivered_at_offset_minutes)
            VALUES (
                '{envelope.Address.Value}', '{envelope.MessageId.Value}', {state},
                {Now.UtcTicks}, {(int)Now.Offset.TotalMinutes},
                {SqlText(isLeased ? MigrationLeaseToken.ToString("N") : null)},
                {SqlText(isLeased ? "migration-worker" : null)},
                {SqlNumber(isLeased ? Now.UtcTicks : (long?)null)},
                {SqlNumber(isLeased ? (long)Now.Offset.TotalMinutes : (long?)null)},
                {SqlNumber(isLeased ? Now.AddMinutes(10).UtcTicks : (long?)null)},
                {SqlNumber(isLeased ? (long)Now.Offset.TotalMinutes : (long?)null)},
                {attempt},
                {SqlNumber(isCompleted ? Now.AddMinutes(15).UtcTicks : (long?)null)},
                {SqlNumber(isCompleted ? (long)Now.Offset.TotalMinutes : (long?)null)});
            """);
    }

    private static async ValueTask AssertMigratedVersionAndStateAsync(
        TemporarySqliteDatabase database,
        int expectedState,
        int expectedAttempt,
        string? expectedLeaseToken,
        string? expectedOwner,
        long? expectedDeliveredAtUtcTicks)
    {
        await using var connection = await SqlFileDurableOutputTestDatabase.OpenAsync(
            database.DatabasePath);
        (await SqlFileDurableOutputTestDatabase.ScalarAsync<long>(
            connection,
            "SELECT version FROM fluxflow_durable_output_delivery_schema WHERE singleton = 1;"))
            .ShouldBe(2);
        (await SqlFileDurableOutputTestDatabase.ScalarAsync<long>(
            connection,
            "SELECT COUNT(*) FROM pragma_table_info('fluxflow_durable_output_deliveries');"))
            .ShouldBe(18);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT state, attempt, lease_token, lease_owner, delivered_at_utc_ticks,
                   dead_letter_reason, dead_lettered_at_utc_ticks,
                   dead_lettered_at_offset_minutes, dead_letter_generation
            FROM fluxflow_durable_output_deliveries;
            """;
        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).ShouldBeTrue();
        reader.GetInt32(0).ShouldBe(expectedState);
        reader.GetInt32(1).ShouldBe(expectedAttempt);
        (reader.IsDBNull(2) ? null : reader.GetString(2)).ShouldBe(expectedLeaseToken);
        (reader.IsDBNull(3) ? null : reader.GetString(3)).ShouldBe(expectedOwner);
        (reader.IsDBNull(4) ? (long?)null : reader.GetInt64(4))
            .ShouldBe(expectedDeliveredAtUtcTicks);
        reader.IsDBNull(5).ShouldBeTrue();
        reader.IsDBNull(6).ShouldBeTrue();
        reader.IsDBNull(7).ShouldBeTrue();
        reader.GetInt64(8).ShouldBe(0);
        (await reader.ReadAsync()).ShouldBeFalse();
    }

    private static async ValueTask CreateValidDeliverySchemaAsync(
        TemporarySqliteDatabase database)
    {
        await using var store = database.CreateStore();
        await store.EnqueueAsync(DurableOutputStoreConformanceData.Envelope());
        (await store.TryLeaseAsync(Request())).ShouldNotBeNull();
    }

    private static DurableOutputDeliveryLeaseRequest Request()
        => new("schema-worker", Now, Now.AddSeconds(30));
}
