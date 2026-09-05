using System.Globalization;
using Microsoft.Data.Sqlite;

namespace FluxFlow.Engine.DurableInput.SqlFile;

internal static class SqlFileDurableInputSchema
{
    public const int CurrentVersion = 2;

    private const string InputTableName = "fluxflow_durable_inputs";
    private const string DeadLetterIndexName = "ix_fluxflow_durable_inputs_dead_lettered";

    public static async ValueTask InitializeAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var transaction = connection.BeginTransaction(deferred: false);
        await CreateSchemaTableAsync(connection, transaction, cancellationToken)
            .ConfigureAwait(false);
        var version = await ReadVersionAsync(connection, transaction, cancellationToken)
            .ConfigureAwait(false);

        if (version is null)
        {
            if (await ObjectExistsAsync(
                    connection,
                    transaction,
                    "table",
                    InputTableName,
                    cancellationToken)
                .ConfigureAwait(false))
            {
                throw new InvalidDataException(
                    "SQL-file durable input database contains an unversioned durable-input table.");
            }

            await CreateVersionTwoSchemaAsync(connection, transaction, cancellationToken)
                .ConfigureAwait(false);
            await InsertVersionAsync(
                    connection,
                    transaction,
                    CurrentVersion,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        else if (version > CurrentVersion)
        {
            throw new NotSupportedException(
                $"SQL-file durable input schema version {version} is newer than supported version {CurrentVersion}.");
        }
        else if (version < CurrentVersion)
        {
            await MigrateAsync(connection, transaction, version.Value, cancellationToken)
                .ConfigureAwait(false);
        }

        await ValidateCurrentSchemaAsync(connection, transaction, cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private static async ValueTask CreateSchemaTableAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS fluxflow_durable_input_schema (
                singleton INTEGER NOT NULL PRIMARY KEY CHECK (singleton = 1),
                version INTEGER NOT NULL CHECK (version > 0)
            ) WITHOUT ROWID;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask CreateVersionTwoSchemaAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE fluxflow_durable_inputs (
                application_address TEXT COLLATE BINARY NOT NULL,
                message_id TEXT COLLATE BINARY NOT NULL,
                contract_name TEXT COLLATE BINARY NOT NULL CHECK (length(contract_name) > 0),
                envelope_schema_version INTEGER NOT NULL CHECK (envelope_schema_version > 0),
                is_error INTEGER NOT NULL CHECK (is_error IN (0, 1)),
                payload_json TEXT NOT NULL,
                error_code TEXT NULL,
                error_message TEXT NULL,
                error_category TEXT NULL,
                error_is_transient INTEGER NULL CHECK (error_is_transient IS NULL OR error_is_transient IN (0, 1)),
                error_details_json TEXT NULL,
                trace_id TEXT NOT NULL,
                correlation_id TEXT NULL,
                causation_id TEXT NULL,
                message_timestamp_utc_ticks INTEGER NOT NULL,
                message_timestamp_offset_minutes INTEGER NOT NULL
                    CHECK (message_timestamp_offset_minutes BETWEEN -840 AND 840),
                enqueued_at_utc_ticks INTEGER NOT NULL,
                enqueued_at_offset_minutes INTEGER NOT NULL
                    CHECK (enqueued_at_offset_minutes BETWEEN -840 AND 840),
                headers_json TEXT NOT NULL,
                state INTEGER NOT NULL CHECK (state IN (0, 1, 2, 3)),
                attempt INTEGER NOT NULL CHECK (attempt >= 0),
                next_attempt_utc_ticks INTEGER NULL,
                lease_owner TEXT NULL,
                lease_token TEXT NULL,
                leased_at_utc_ticks INTEGER NULL,
                lease_until_utc_ticks INTEGER NULL,
                failure_kind INTEGER NULL,
                failure_description TEXT NULL,
                delivered_at_utc_ticks INTEGER NULL,
                dead_lettered_at_utc_ticks INTEGER NULL,
                dead_letter_generation INTEGER NOT NULL DEFAULT 0
                    CHECK (dead_letter_generation >= 0),
                PRIMARY KEY (application_address, message_id),
                CHECK ((is_error = 0
                        AND error_code IS NULL
                        AND error_message IS NULL
                        AND error_category IS NULL
                        AND error_is_transient IS NULL
                        AND error_details_json IS NULL)
                    OR (is_error = 1
                        AND error_code IS NOT NULL
                        AND error_message IS NOT NULL
                        AND error_category IS NOT NULL
                        AND error_is_transient IS NOT NULL)),
                CHECK ((failure_kind IS NULL AND failure_description IS NULL)
                    OR (failure_kind IS NOT NULL AND failure_description IS NOT NULL)),
                CHECK ((state = 0
                        AND next_attempt_utc_ticks IS NOT NULL
                        AND lease_owner IS NULL
                        AND lease_token IS NULL
                        AND leased_at_utc_ticks IS NULL
                        AND lease_until_utc_ticks IS NULL
                        AND delivered_at_utc_ticks IS NULL
                        AND dead_lettered_at_utc_ticks IS NULL)
                    OR (state = 1
                        AND attempt > 0
                        AND next_attempt_utc_ticks IS NULL
                        AND lease_owner IS NOT NULL
                        AND lease_token IS NOT NULL
                        AND leased_at_utc_ticks IS NOT NULL
                        AND lease_until_utc_ticks IS NOT NULL
                        AND lease_until_utc_ticks > leased_at_utc_ticks
                        AND delivered_at_utc_ticks IS NULL
                        AND dead_lettered_at_utc_ticks IS NULL)
                    OR (state = 2
                        AND next_attempt_utc_ticks IS NULL
                        AND lease_owner IS NULL
                        AND lease_token IS NULL
                        AND leased_at_utc_ticks IS NULL
                        AND lease_until_utc_ticks IS NULL
                        AND delivered_at_utc_ticks IS NOT NULL
                        AND dead_lettered_at_utc_ticks IS NULL)
                    OR (state = 3
                        AND next_attempt_utc_ticks IS NULL
                        AND lease_owner IS NULL
                        AND lease_token IS NULL
                        AND leased_at_utc_ticks IS NULL
                        AND lease_until_utc_ticks IS NULL
                        AND failure_kind IS NOT NULL
                        AND delivered_at_utc_ticks IS NULL
                        AND dead_lettered_at_utc_ticks IS NOT NULL
                        AND dead_letter_generation > 0))
            ) WITHOUT ROWID;

            CREATE INDEX ix_fluxflow_durable_inputs_pending_due
                ON fluxflow_durable_inputs (
                    state,
                    next_attempt_utc_ticks,
                    enqueued_at_utc_ticks,
                    application_address,
                    message_id)
                WHERE state = 0;

            CREATE INDEX ix_fluxflow_durable_inputs_lease_expiry
                ON fluxflow_durable_inputs (
                    state,
                    lease_until_utc_ticks,
                    enqueued_at_utc_ticks,
                    application_address,
                    message_id)
                WHERE state = 1;

            CREATE INDEX ix_fluxflow_durable_inputs_dead_lettered
                ON fluxflow_durable_inputs (
                    state,
                    dead_lettered_at_utc_ticks DESC,
                    application_address,
                    message_id)
                WHERE state = 3;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask MigrateAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int version,
        CancellationToken cancellationToken)
    {
        if (version != 1)
        {
            throw new NotSupportedException(
                $"SQL-file durable input schema version {version} cannot be migrated to version {CurrentVersion}.");
        }

        if (!await ObjectExistsAsync(
                connection,
                transaction,
                "table",
                InputTableName,
                cancellationToken)
            .ConfigureAwait(false))
        {
            throw new InvalidDataException(
                "SQL-file durable input schema metadata exists but the durable-input table is missing.");
        }

        if (await ReadColumnAsync(
                connection,
                transaction,
                "dead_letter_generation",
                cancellationToken)
            .ConfigureAwait(false) is not null)
        {
            throw new InvalidDataException(
                "SQL-file durable input version-1 schema unexpectedly contains the version-2 dead-letter generation column.");
        }

        await using (var schemaCommand = connection.CreateCommand())
        {
            schemaCommand.Transaction = transaction;
            schemaCommand.CommandText = """
                ALTER TABLE fluxflow_durable_inputs
                    ADD COLUMN dead_letter_generation INTEGER NOT NULL DEFAULT 0
                        CHECK (dead_letter_generation >= 0);

                UPDATE fluxflow_durable_inputs
                SET dead_letter_generation = 1
                WHERE state = 3;

                CREATE INDEX ix_fluxflow_durable_inputs_dead_lettered
                    ON fluxflow_durable_inputs (
                        state,
                        dead_lettered_at_utc_ticks DESC,
                        application_address,
                        message_id)
                    WHERE state = 3;
                """;
            await schemaCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        await using var versionCommand = connection.CreateCommand();
        versionCommand.Transaction = transaction;
        versionCommand.CommandText = """
            UPDATE fluxflow_durable_input_schema
            SET version = 2
            WHERE singleton = 1 AND version = 1;
            """;
        var affected = await versionCommand.ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false);
        if (affected != 1)
            throw new InvalidDataException("SQL-file durable input schema migration did not update its version.");
    }

    private static async ValueTask ValidateCurrentSchemaAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        if (!await ObjectExistsAsync(
                connection,
                transaction,
                "table",
                InputTableName,
                cancellationToken)
            .ConfigureAwait(false))
        {
            throw new InvalidDataException(
                "SQL-file durable input schema metadata exists but the durable-input table is missing.");
        }

        var generationColumn = await ReadColumnAsync(
                connection,
                transaction,
                "dead_letter_generation",
                cancellationToken)
            .ConfigureAwait(false);
        if (generationColumn is null)
        {
            throw new InvalidDataException(
                "SQL-file durable input schema version 2 is missing the dead-letter generation column.");
        }

        if (!string.Equals(generationColumn.Type, "INTEGER", StringComparison.OrdinalIgnoreCase) ||
            !generationColumn.IsNotNull ||
            !string.Equals(generationColumn.DefaultValue, "0", StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "SQL-file durable input schema version 2 has an incompatible dead-letter generation column.");
        }

        if (!await ObjectExistsAsync(
                connection,
                transaction,
                "index",
                DeadLetterIndexName,
                cancellationToken)
            .ConfigureAwait(false))
        {
            throw new InvalidDataException(
                "SQL-file durable input schema version 2 is missing the dead-letter index.");
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT application_address, message_id
            FROM fluxflow_durable_inputs
            WHERE dead_letter_generation < 0
               OR (state = 3 AND dead_letter_generation <= 0)
            LIMIT 1;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidDataException(
                $"SQL-file durable input row '{reader.GetString(0)}#{reader.GetString(1)}' has an invalid dead-letter generation.");
        }
    }

    private static async ValueTask<int?> ReadVersionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT version FROM fluxflow_durable_input_schema WHERE singleton = 1;";
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is null or DBNull
            ? null
            : Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    private static async ValueTask InsertVersionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int version,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO fluxflow_durable_input_schema (singleton, version)
            VALUES (1, $version);
            """;
        command.Parameters.AddWithValue("$version", version);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask<bool> ObjectExistsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string type,
        string name,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT 1
            FROM sqlite_schema
            WHERE type = $type AND name = $name;
            """;
        command.Parameters.AddWithValue("$type", type);
        command.Parameters.AddWithValue("$name", name);
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null;
    }

    private static async ValueTask<ColumnDefinition?> ReadColumnAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string columnName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "PRAGMA table_info('fluxflow_durable_inputs');";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.Ordinal))
            {
                return new ColumnDefinition(
                    reader.GetString(2),
                    reader.GetInt32(3) == 1,
                    reader.IsDBNull(4) ? null : reader.GetString(4));
            }
        }

        return null;
    }

    private sealed record ColumnDefinition(
        string Type,
        bool IsNotNull,
        string? DefaultValue);
}
