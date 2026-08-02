using FluxFlow.Engine.DurableInput.Tests;
using Microsoft.Data.Sqlite;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.DurableInput.SqlFile.Tests;

public sealed class SqlFileDurableInputDeadLetterMigrationTests
{
    [Fact]
    public async Task Version_one_rows_migrate_to_version_two_with_state_and_envelope_preserved()
    {
        using var database = TemporarySqliteDatabase.Create();
        var pending = DurableInputStoreConformanceData.Envelope(
            "migration-pending",
            enqueuedAt: DurableInputStoreConformanceData.Now.AddDays(2));
        var released = DurableInputStoreConformanceData.Envelope(
            "migration-released",
            enqueuedAt: DurableInputStoreConformanceData.Now.AddMinutes(-4));
        var leased = DurableInputStoreConformanceData.Envelope(
            "migration-leased",
            enqueuedAt: DurableInputStoreConformanceData.Now.AddMinutes(-3));
        var delivered = DurableInputStoreConformanceData.Envelope(
            "migration-delivered",
            enqueuedAt: DurableInputStoreConformanceData.Now.AddMinutes(-2));
        var dead = SqlFileDurableInputTestData.CompleteErrorEnvelope("migration-dead");
        var failure = DurableInputStoreConformanceData.Failure(
            DurableInputFailureKind.DeserializationFailed,
            "v1 dead-letter failure");
        await using (var writer = database.CreateStore())
        {
            await writer.EnqueueAsync(pending);
            await writer.EnqueueAsync(released);
            var releasedLease = (await writer.LeaseAsync(DurableInputStoreConformanceData.Request(
                now: released.EnqueuedAt,
                leaseUntil: released.EnqueuedAt.AddMinutes(1)))).Single();
            (await writer.ReleaseAsync(new DurableInputRelease(
                released.Key,
                releasedLease.LeaseToken,
                released.EnqueuedAt.AddSeconds(1),
                DurableInputStoreConformanceData.Now.AddDays(2),
                DurableInputStoreConformanceData.Failure())))
                .Status.ShouldBe(DurableInputTransitionStatus.Applied);

            await writer.EnqueueAsync(leased);
            var activeLease = (await writer.LeaseAsync(DurableInputStoreConformanceData.Request(
                ownerId: "migration-owner",
                now: leased.EnqueuedAt,
                leaseUntil: DurableInputStoreConformanceData.Now.AddDays(2)))).Single();
            activeLease.Envelope.Key.ShouldBe(leased.Key);

            await writer.EnqueueAsync(delivered);
            var deliveredLease = (await writer.LeaseAsync(DurableInputStoreConformanceData.Request(
                now: delivered.EnqueuedAt,
                leaseUntil: delivered.EnqueuedAt.AddMinutes(1)))).Single();
            (await writer.MarkDeliveredAsync(new DurableInputLeaseTransition(
                delivered.Key,
                deliveredLease.LeaseToken,
                delivered.EnqueuedAt.AddSeconds(1))))
                .Status.ShouldBe(DurableInputTransitionStatus.Applied);

            await writer.EnqueueAsync(dead);
            var deadLease = (await writer.LeaseAsync(DurableInputStoreConformanceData.Request(
                now: dead.EnqueuedAt,
                leaseUntil: dead.EnqueuedAt.AddMinutes(2)))).Single();
            (await writer.DeadLetterAsync(new DurableInputDeadLetter(
                dead.Key,
                deadLease.LeaseToken,
                dead.EnqueuedAt.AddMinutes(1),
                failure))).Status.ShouldBe(DurableInputTransitionStatus.Applied);
        }

        await SqlFileVersionOneDatabase.DowngradeAsync(database.DatabasePath);
        var before = await ReadRowsAsync(database.DatabasePath);
        before.Select(static row => (row.MessageId, row.State, row.Attempt)).ShouldBe([
            ("migration-dead", DurableInputState.DeadLettered, 1),
            ("migration-delivered", DurableInputState.Delivered, 1),
            ("migration-leased", DurableInputState.Leased, 1),
            ("migration-pending", DurableInputState.Pending, 0),
            ("migration-released", DurableInputState.Pending, 1)
        ]);

        await using var migrated = database.CreateStore();
        var page = await migrated.ListAsync(new DurableInputDeadLetterQuery());
        var details = await migrated.GetAsync(dead.Key);
        var after = await ReadRowsAsync(database.DatabasePath);

        page.Items.ShouldHaveSingleItem().Key.ShouldBe(dead.Key);
        details.ShouldNotBeNull();
        details.Envelope.ShouldMatchEnvelope(dead);
        details.Failure.ShouldBe(failure);
        details.Attempt.ShouldBe(1);
        details.Generation.ShouldBe(1);
        after.Select(static row => (row.MessageId, row.State, row.Attempt)).ShouldBe(
            before.Select(static row => (row.MessageId, row.State, row.Attempt)));
        after.Select(static row => row with { Generation = 0 }).ShouldBe(before);
        after.Single(static row => row.MessageId == "migration-dead").Generation.ShouldBe(1);
        after.Where(static row => row.MessageId != "migration-dead")
            .ShouldAllBe(static row => row.Generation == 0);
        (await ScalarAsync<long>(
            database.DatabasePath,
            "SELECT version FROM fluxflow_durable_input_schema WHERE singleton = 1;"))
            .ShouldBe(2);
        (await ScalarAsync<long>(
            database.DatabasePath,
            "SELECT COUNT(*) FROM sqlite_schema WHERE type = 'index' AND name = 'ix_fluxflow_durable_inputs_dead_lettered';"))
            .ShouldBe(1);
        await SqlFileDeadLetterSchemaAssertions.ShouldHaveExactVersionTwoShapeAsync(
            database.DatabasePath);
    }

    [Fact]
    public async Task Concurrent_first_use_migrates_version_one_once_and_both_instances_observe_the_dead_letter()
    {
        using var database = TemporarySqliteDatabase.Create();
        var envelope = DurableInputStoreConformanceData.Envelope("concurrent-migration");
        await using (var writer = database.CreateStore())
        {
            await DeadLetterAsync(writer, envelope);
        }

        await SqlFileVersionOneDatabase.DowngradeAsync(database.DatabasePath);
        await using var first = database.CreateStore();
        await using var second = database.CreateStore();

        var pages = await Task.WhenAll(
            first.ListAsync(new DurableInputDeadLetterQuery()).AsTask(),
            second.ListAsync(new DurableInputDeadLetterQuery()).AsTask());

        pages.ShouldAllBe(page => page.Items.Count == 1 && page.Items[0].Key == envelope.Key);
        pages.ShouldAllBe(page => page.Items[0].Generation == 1);
        (await ScalarAsync<long>(
            database.DatabasePath,
            "SELECT version FROM fluxflow_durable_input_schema WHERE singleton = 1;"))
            .ShouldBe(2);
        (await ScalarAsync<long>(
            database.DatabasePath,
            "SELECT COUNT(*) FROM sqlite_schema WHERE type = 'index' AND name = 'ix_fluxflow_durable_inputs_dead_lettered';"))
            .ShouldBe(1);
    }

    [Fact]
    public async Task Precancelled_version_one_first_use_leaves_schema_unchanged_then_same_store_migrates()
    {
        using var database = TemporarySqliteDatabase.Create();
        await using (var initializer = database.CreateStore())
        {
            (await initializer.ListAsync(new DurableInputDeadLetterQuery())).Items.ShouldBeEmpty();
        }

        await SqlFileVersionOneDatabase.DowngradeAsync(database.DatabasePath);
        await using var store = database.CreateStore();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(() =>
            store.ListAsync(new DurableInputDeadLetterQuery(), cancellation.Token).AsTask());

        (await ScalarAsync<long>(
            database.DatabasePath,
            "SELECT version FROM fluxflow_durable_input_schema WHERE singleton = 1;"))
            .ShouldBe(1);
        (await ColumnExistsAsync(database.DatabasePath, "dead_letter_generation")).ShouldBeFalse();
        (await ScalarAsync<long>(
            database.DatabasePath,
            "SELECT COUNT(*) FROM sqlite_schema WHERE type = 'index' AND name = 'ix_fluxflow_durable_inputs_dead_lettered';"))
            .ShouldBe(0);

        (await store.ListAsync(new DurableInputDeadLetterQuery())).Items.ShouldBeEmpty();
        (await ScalarAsync<long>(
            database.DatabasePath,
            "SELECT version FROM fluxflow_durable_input_schema WHERE singleton = 1;"))
            .ShouldBe(2);
        (await ColumnExistsAsync(database.DatabasePath, "dead_letter_generation")).ShouldBeTrue();
        (await ScalarAsync<long>(
            database.DatabasePath,
            "SELECT COUNT(*) FROM sqlite_schema WHERE type = 'index' AND name = 'ix_fluxflow_durable_inputs_dead_lettered';"))
            .ShouldBe(1);
    }

    [Fact]
    public async Task Failed_migration_rolls_back_every_change_and_same_store_recovers_after_repair()
    {
        using var database = TemporarySqliteDatabase.Create();
        await using (var initializer = database.CreateStore())
        {
            (await initializer.ListAsync(new DurableInputDeadLetterQuery())).Items.ShouldBeEmpty();
        }

        await SqlFileVersionOneDatabase.DowngradeAsync(database.DatabasePath);
        await ExecuteAsync(
            database.DatabasePath,
            "CREATE TABLE ix_fluxflow_durable_inputs_dead_lettered (foreign_value INTEGER NOT NULL);");
        await using var store = database.CreateStore();

        var exception = await Should.ThrowAsync<InvalidDataException>(
            () => store.ListAsync(new DurableInputDeadLetterQuery()).AsTask());

        exception.Message.ShouldContain("dead-letter index");
        (await ScalarAsync<long>(
            database.DatabasePath,
            "SELECT version FROM fluxflow_durable_input_schema WHERE singleton = 1;"))
            .ShouldBe(1);
        (await ColumnExistsAsync(database.DatabasePath, "dead_letter_generation")).ShouldBeFalse();
        await ExecuteAsync(
            database.DatabasePath,
            "DROP TABLE ix_fluxflow_durable_inputs_dead_lettered;");

        (await store.ListAsync(new DurableInputDeadLetterQuery())).Items.ShouldBeEmpty();
        (await ScalarAsync<long>(
            database.DatabasePath,
            "SELECT version FROM fluxflow_durable_input_schema WHERE singleton = 1;"))
            .ShouldBe(2);
        (await ColumnExistsAsync(database.DatabasePath, "dead_letter_generation")).ShouldBeTrue();
    }

    [Fact]
    public async Task Version_one_schema_with_version_two_generation_column_is_rejected_without_upgrade()
    {
        using var database = TemporarySqliteDatabase.Create();
        await using (var initializer = database.CreateStore())
        {
            (await initializer.ListAsync(new DurableInputDeadLetterQuery())).Items.ShouldBeEmpty();
        }

        await SqlFileVersionOneDatabase.DowngradeAsync(database.DatabasePath);
        await ExecuteAsync(
            database.DatabasePath,
            "ALTER TABLE fluxflow_durable_inputs ADD COLUMN dead_letter_generation INTEGER NOT NULL DEFAULT 0;");
        await using var store = database.CreateStore();

        var exception = await Should.ThrowAsync<InvalidDataException>(
            () => store.ListAsync(new DurableInputDeadLetterQuery()).AsTask());

        exception.Message.ShouldContain("version-1");
        exception.Message.ShouldContain("dead-letter generation column");
        (await ScalarAsync<long>(
            database.DatabasePath,
            "SELECT version FROM fluxflow_durable_input_schema WHERE singleton = 1;"))
            .ShouldBe(1);
    }

    [Fact]
    public async Task Version_two_schema_missing_dead_letter_index_is_rejected_without_repair()
    {
        using var database = TemporarySqliteDatabase.Create();
        await using (var initializer = database.CreateStore())
        {
            (await initializer.ListAsync(new DurableInputDeadLetterQuery())).Items.ShouldBeEmpty();
        }

        await ExecuteAsync(
            database.DatabasePath,
            "DROP INDEX ix_fluxflow_durable_inputs_dead_lettered;");
        await using var store = database.CreateStore();

        var exception = await Should.ThrowAsync<InvalidDataException>(
            () => store.ListAsync(new DurableInputDeadLetterQuery()).AsTask());

        exception.Message.ShouldContain("version 2");
        exception.Message.ShouldContain("dead-letter index");
        (await ScalarAsync<long>(
            database.DatabasePath,
            "SELECT version FROM fluxflow_durable_input_schema WHERE singleton = 1;"))
            .ShouldBe(2);
        (await ScalarAsync<long>(
            database.DatabasePath,
            "SELECT COUNT(*) FROM sqlite_schema WHERE type = 'index' AND name = 'ix_fluxflow_durable_inputs_dead_lettered';"))
            .ShouldBe(0);
    }

    [Fact]
    public async Task Version_two_schema_missing_generation_column_is_rejected_without_repair()
    {
        using var database = TemporarySqliteDatabase.Create();
        await using (var initializer = database.CreateStore())
        {
            (await initializer.ListAsync(new DurableInputDeadLetterQuery())).Items.ShouldBeEmpty();
        }

        await SqlFileVersionOneDatabase.DowngradeAsync(database.DatabasePath);
        await ExecuteAsync(
            database.DatabasePath,
            "UPDATE fluxflow_durable_input_schema SET version = 2 WHERE singleton = 1;");
        await using var store = database.CreateStore();

        var exception = await Should.ThrowAsync<InvalidDataException>(
            () => store.ListAsync(new DurableInputDeadLetterQuery()).AsTask());

        exception.Message.ShouldContain("version 2");
        exception.Message.ShouldContain("dead-letter generation column");
        (await ScalarAsync<long>(
            database.DatabasePath,
            "SELECT version FROM fluxflow_durable_input_schema WHERE singleton = 1;"))
            .ShouldBe(2);
        (await ColumnExistsAsync(database.DatabasePath, "dead_letter_generation")).ShouldBeFalse();
    }

    private static async ValueTask DeadLetterAsync(
        SqlFileDurableInputStore store,
        DurableInputEnvelope envelope)
    {
        await store.EnqueueAsync(envelope);
        var lease = (await store.LeaseAsync(DurableInputStoreConformanceData.Request(
            now: envelope.EnqueuedAt,
            leaseUntil: envelope.EnqueuedAt.AddMinutes(2)))).Single();
        (await store.DeadLetterAsync(new DurableInputDeadLetter(
            envelope.Key,
            lease.LeaseToken,
            envelope.EnqueuedAt.AddMinutes(1),
            DurableInputStoreConformanceData.Failure())))
            .Status.ShouldBe(DurableInputTransitionStatus.Applied);
    }

    private static async ValueTask<IReadOnlyList<PersistedRow>> ReadRowsAsync(string path)
    {
        var hasGeneration = await ColumnExistsAsync(path, "dead_letter_generation");
        await using var connection = await OpenAsync(path);
        await using var command = connection.CreateCommand();
        command.CommandText = hasGeneration
            ? """
                SELECT message_id,
                       state,
                       attempt,
                       next_attempt_utc_ticks,
                       lease_owner,
                       lease_token,
                       leased_at_utc_ticks,
                       lease_until_utc_ticks,
                       failure_kind,
                       failure_description,
                       delivered_at_utc_ticks,
                       dead_lettered_at_utc_ticks,
                       dead_letter_generation
                FROM fluxflow_durable_inputs
                ORDER BY message_id;
                """
            : """
                SELECT message_id,
                       state,
                       attempt,
                       next_attempt_utc_ticks,
                       lease_owner,
                       lease_token,
                       leased_at_utc_ticks,
                       lease_until_utc_ticks,
                       failure_kind,
                       failure_description,
                       delivered_at_utc_ticks,
                       dead_lettered_at_utc_ticks,
                       0
                FROM fluxflow_durable_inputs
                ORDER BY message_id;
                """;
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<PersistedRow>();
        while (await reader.ReadAsync())
        {
            rows.Add(new PersistedRow(
                reader.GetString(0),
                (DurableInputState)reader.GetInt32(1),
                reader.GetInt32(2),
                NullableInt64(reader, 3),
                NullableString(reader, 4),
                NullableString(reader, 5),
                NullableInt64(reader, 6),
                NullableInt64(reader, 7),
                reader.IsDBNull(8) ? null : reader.GetInt32(8),
                NullableString(reader, 9),
                NullableInt64(reader, 10),
                NullableInt64(reader, 11),
                reader.GetInt64(12)));
        }

        return rows;
    }

    private static async ValueTask ExecuteAsync(string path, string commandText)
    {
        await using var connection = await OpenAsync(path);
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync();
    }

    private static async ValueTask<T> ScalarAsync<T>(string path, string commandText)
    {
        await using var connection = await OpenAsync(path);
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        var result = await command.ExecuteScalarAsync();
        return (T)Convert.ChangeType(result!, typeof(T));
    }

    private static async ValueTask<bool> ColumnExistsAsync(string path, string columnName)
    {
        await using var connection = await OpenAsync(path);
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info('fluxflow_durable_inputs');";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static async ValueTask<SqliteConnection> OpenAsync(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false
        }.ToString());
        await connection.OpenAsync();
        return connection;
    }

    private sealed record PersistedRow(
        string MessageId,
        DurableInputState State,
        int Attempt,
        long? NextAttemptTicks,
        string? LeaseOwner,
        string? LeaseToken,
        long? LeasedAtTicks,
        long? LeaseUntilTicks,
        int? FailureKind,
        string? FailureDescription,
        long? DeliveredAtTicks,
        long? DeadLetteredAtTicks,
        long Generation);

    private static long? NullableInt64(SqliteDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : reader.GetInt64(ordinal);

    private static string? NullableString(SqliteDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
}
