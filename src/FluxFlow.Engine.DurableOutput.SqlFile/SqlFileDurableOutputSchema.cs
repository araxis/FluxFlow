using System.Globalization;
using Microsoft.Data.Sqlite;

namespace FluxFlow.Engine.DurableOutput.SqlFile;

internal static class SqlFileDurableOutputSchema
{
    public const int CurrentVersion = 1;

    internal const string SchemaTableName = "fluxflow_durable_output_schema";
    internal const string OutputTableName = "fluxflow_durable_outputs";

    private static readonly ColumnDefinition[] SchemaColumns =
    [
        new("singleton", "INTEGER", IsNotNull: true, PrimaryKeyOrdinal: 1),
        new("version", "INTEGER", IsNotNull: true, PrimaryKeyOrdinal: 0)
    ];

    private static readonly ColumnDefinition[] OutputColumns =
    [
        new("application_address", "TEXT", IsNotNull: true, PrimaryKeyOrdinal: 1),
        new("message_id", "TEXT", IsNotNull: true, PrimaryKeyOrdinal: 2),
        new("contract_name", "TEXT", IsNotNull: true, PrimaryKeyOrdinal: 0),
        new("envelope_schema_version", "INTEGER", IsNotNull: true, PrimaryKeyOrdinal: 0),
        new("is_error", "INTEGER", IsNotNull: true, PrimaryKeyOrdinal: 0),
        new("payload_json", "TEXT", IsNotNull: true, PrimaryKeyOrdinal: 0),
        new("error_code", "TEXT", IsNotNull: false, PrimaryKeyOrdinal: 0),
        new("error_message", "TEXT", IsNotNull: false, PrimaryKeyOrdinal: 0),
        new("error_category", "TEXT", IsNotNull: false, PrimaryKeyOrdinal: 0),
        new("error_is_transient", "INTEGER", IsNotNull: false, PrimaryKeyOrdinal: 0),
        new("error_details_json", "TEXT", IsNotNull: false, PrimaryKeyOrdinal: 0),
        new("trace_id", "TEXT", IsNotNull: true, PrimaryKeyOrdinal: 0),
        new("correlation_id", "TEXT", IsNotNull: false, PrimaryKeyOrdinal: 0),
        new("causation_id", "TEXT", IsNotNull: false, PrimaryKeyOrdinal: 0),
        new("message_timestamp_utc_ticks", "INTEGER", IsNotNull: true, PrimaryKeyOrdinal: 0),
        new("message_timestamp_offset_minutes", "INTEGER", IsNotNull: true, PrimaryKeyOrdinal: 0),
        new("captured_at_utc_ticks", "INTEGER", IsNotNull: true, PrimaryKeyOrdinal: 0),
        new("captured_at_offset_minutes", "INTEGER", IsNotNull: true, PrimaryKeyOrdinal: 0),
        new("headers_json", "TEXT", IsNotNull: true, PrimaryKeyOrdinal: 0)
    ];

    public static async ValueTask InitializeAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var transaction = connection.BeginTransaction(deferred: false);
        var hasSchema = await ObjectExistsAsync(
                connection,
                transaction,
                "table",
                SchemaTableName,
                cancellationToken)
            .ConfigureAwait(false);

        if (!hasSchema)
        {
            if (await ObjectExistsAsync(
                    connection,
                    transaction,
                    "table",
                    OutputTableName,
                    cancellationToken)
                .ConfigureAwait(false))
            {
                throw new InvalidDataException(
                    "SQL-file durable output database contains an unversioned durable-output table.");
            }

            await CreateVersionOneSchemaAsync(connection, transaction, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            await ValidateColumnsAsync(
                    connection,
                    transaction,
                    SchemaTableName,
                    SchemaColumns,
                    cancellationToken)
                .ConfigureAwait(false);

            var version = await ReadVersionAsync(connection, transaction, cancellationToken)
                .ConfigureAwait(false);
            if (version > CurrentVersion)
            {
                throw new NotSupportedException(
                    $"SQL-file durable output schema version {version} is newer than supported version {CurrentVersion}.");
            }

            if (version < CurrentVersion)
            {
                throw new NotSupportedException(
                    $"SQL-file durable output schema version {version} cannot be migrated to version {CurrentVersion}.");
            }
        }

        await ValidateColumnsAsync(
                connection,
                transaction,
                OutputTableName,
                OutputColumns,
                cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private static async ValueTask CreateVersionOneSchemaAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE fluxflow_durable_output_schema (
                singleton INTEGER NOT NULL PRIMARY KEY CHECK (singleton = 1),
                version INTEGER NOT NULL CHECK (version > 0)
            ) WITHOUT ROWID;

            CREATE TABLE fluxflow_durable_outputs (
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
                trace_id TEXT COLLATE BINARY NOT NULL,
                correlation_id TEXT COLLATE BINARY NULL,
                causation_id TEXT COLLATE BINARY NULL,
                message_timestamp_utc_ticks INTEGER NOT NULL,
                message_timestamp_offset_minutes INTEGER NOT NULL
                    CHECK (message_timestamp_offset_minutes BETWEEN -840 AND 840),
                captured_at_utc_ticks INTEGER NOT NULL,
                captured_at_offset_minutes INTEGER NOT NULL
                    CHECK (captured_at_offset_minutes BETWEEN -840 AND 840),
                headers_json TEXT NOT NULL,
                PRIMARY KEY (application_address, message_id),
                CHECK ((is_error = 0
                        AND error_code IS NULL
                        AND error_message IS NULL
                        AND error_category IS NULL
                        AND error_is_transient IS NULL
                        AND error_details_json IS NULL)
                    OR (is_error = 1
                        AND payload_json = 'null'
                        AND error_code IS NOT NULL
                        AND error_message IS NOT NULL
                        AND error_category IS NOT NULL
                        AND error_is_transient IS NOT NULL))
            ) WITHOUT ROWID;

            INSERT INTO fluxflow_durable_output_schema (singleton, version)
            VALUES (1, 1);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask<int> ReadVersionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT singleton, version FROM fluxflow_durable_output_schema;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            throw new InvalidDataException("SQL-file durable output schema version is missing.");

        var singleton = reader.GetInt32(0);
        var version = reader.GetInt32(1);
        if (singleton != 1 || version <= 0)
            throw new InvalidDataException("SQL-file durable output schema version metadata is invalid.");
        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            throw new InvalidDataException("SQL-file durable output schema contains multiple version rows.");

        return version;
    }

    private static async ValueTask ValidateColumnsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string table,
        IReadOnlyList<ColumnDefinition> expected,
        CancellationToken cancellationToken)
    {
        if (!await ObjectExistsAsync(
                connection,
                transaction,
                "table",
                table,
                cancellationToken)
            .ConfigureAwait(false))
        {
            throw new InvalidDataException(
                $"SQL-file durable output schema is missing required table '{table}'.");
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = FormattableString.Invariant($"PRAGMA table_info('{table}');");
        var actual = new List<ColumnDefinition>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            actual.Add(new ColumnDefinition(
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3) == 1,
                reader.GetInt32(5)));
        }

        if (actual.Count != expected.Count)
        {
            throw new InvalidDataException(
                $"SQL-file durable output table '{table}' has an incompatible column count.");
        }

        for (var index = 0; index < expected.Count; index++)
        {
            var expectedColumn = expected[index];
            var actualColumn = actual[index];
            if (!string.Equals(actualColumn.Name, expectedColumn.Name, StringComparison.Ordinal) ||
                !string.Equals(actualColumn.Type, expectedColumn.Type, StringComparison.OrdinalIgnoreCase) ||
                actualColumn.IsNotNull != expectedColumn.IsNotNull ||
                actualColumn.PrimaryKeyOrdinal != expectedColumn.PrimaryKeyOrdinal)
            {
                throw new InvalidDataException(
                    $"SQL-file durable output table '{table}' has an incompatible column at ordinal {index}.");
            }
        }
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

    private sealed record ColumnDefinition(
        string Name,
        string Type,
        bool IsNotNull,
        int PrimaryKeyOrdinal);
}
