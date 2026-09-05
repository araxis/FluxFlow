using System.Globalization;
using Microsoft.Data.Sqlite;

namespace FluxFlow.Engine.DurableOutput.SqlFile;

internal static class SqlFileDurableOutputDeliverySchema
{
    public const int CurrentVersion = 2;

    internal const string SchemaTableName = "fluxflow_durable_output_delivery_schema";
    internal const string DeliveryTableName = "fluxflow_durable_output_deliveries";
    internal const string EligibilityIndexName = "ix_fluxflow_durable_output_deliveries_eligibility";
    internal const string DeadLetterIndexName = "ix_fluxflow_durable_output_deliveries_dead_lettered";

    private const string MigrationTableName = "fluxflow_durable_output_deliveries_v2";

    private static readonly ColumnDefinition[] SchemaColumns =
    [
        new("singleton", "INTEGER", IsNotNull: true, PrimaryKeyOrdinal: 1),
        new("version", "INTEGER", IsNotNull: true, PrimaryKeyOrdinal: 0)
    ];

    private static readonly ColumnDefinition[] VersionOneDeliveryColumns =
    [
        new("application_address", "TEXT", IsNotNull: true, PrimaryKeyOrdinal: 1),
        new("message_id", "TEXT", IsNotNull: true, PrimaryKeyOrdinal: 2),
        new("state", "INTEGER", IsNotNull: true, PrimaryKeyOrdinal: 0),
        new("next_attempt_utc_ticks", "INTEGER", IsNotNull: true, PrimaryKeyOrdinal: 0),
        new("next_attempt_offset_minutes", "INTEGER", IsNotNull: true, PrimaryKeyOrdinal: 0),
        new("lease_token", "TEXT", IsNotNull: false, PrimaryKeyOrdinal: 0),
        new("lease_owner", "TEXT", IsNotNull: false, PrimaryKeyOrdinal: 0),
        new("leased_at_utc_ticks", "INTEGER", IsNotNull: false, PrimaryKeyOrdinal: 0),
        new("leased_at_offset_minutes", "INTEGER", IsNotNull: false, PrimaryKeyOrdinal: 0),
        new("lease_until_utc_ticks", "INTEGER", IsNotNull: false, PrimaryKeyOrdinal: 0),
        new("lease_until_offset_minutes", "INTEGER", IsNotNull: false, PrimaryKeyOrdinal: 0),
        new("attempt", "INTEGER", IsNotNull: true, PrimaryKeyOrdinal: 0),
        new("delivered_at_utc_ticks", "INTEGER", IsNotNull: false, PrimaryKeyOrdinal: 0),
        new("delivered_at_offset_minutes", "INTEGER", IsNotNull: false, PrimaryKeyOrdinal: 0)
    ];

    private static readonly ColumnDefinition[] DeliveryColumns =
    [
        .. VersionOneDeliveryColumns,
        new("dead_letter_reason", "INTEGER", IsNotNull: false, PrimaryKeyOrdinal: 0),
        new("dead_lettered_at_utc_ticks", "INTEGER", IsNotNull: false, PrimaryKeyOrdinal: 0),
        new("dead_lettered_at_offset_minutes", "INTEGER", IsNotNull: false, PrimaryKeyOrdinal: 0),
        new(
            "dead_letter_generation",
            "INTEGER",
            IsNotNull: true,
            PrimaryKeyOrdinal: 0,
            DefaultValue: "0")
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
                    DeliveryTableName,
                    cancellationToken)
                .ConfigureAwait(false))
            {
                throw new InvalidDataException(
                    "SQL-file durable output database contains an unversioned delivery table.");
            }

            await CreateVersionTwoSchemaAsync(connection, transaction, cancellationToken)
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
                    $"SQL-file durable output delivery schema version {version} is newer than supported version {CurrentVersion}.");
            }

            if (version == 1)
            {
                await ValidateVersionOneSchemaAsync(connection, transaction, cancellationToken)
                    .ConfigureAwait(false);
                await MigrateVersionOneAsync(connection, transaction, cancellationToken)
                    .ConfigureAwait(false);
            }
            else if (version != CurrentVersion)
            {
                throw new NotSupportedException(
                    $"SQL-file durable output delivery schema version {version} cannot be migrated to version {CurrentVersion}.");
            }
        }

        await ValidateCurrentSchemaAsync(connection, transaction, cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private static async ValueTask CreateVersionTwoSchemaAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                CREATE TABLE fluxflow_durable_output_delivery_schema (
                    singleton INTEGER NOT NULL PRIMARY KEY CHECK (singleton = 1),
                    version INTEGER NOT NULL CHECK (version > 0)
                ) WITHOUT ROWID;
                """;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await CreateVersionTwoDeliveryTableAsync(
                connection,
                transaction,
                DeliveryTableName,
                cancellationToken)
            .ConfigureAwait(false);
        await CreateIndexesAsync(connection, transaction, cancellationToken).ConfigureAwait(false);

        await using var versionCommand = connection.CreateCommand();
        versionCommand.Transaction = transaction;
        versionCommand.CommandText = """
            INSERT INTO fluxflow_durable_output_delivery_schema (singleton, version)
            VALUES (1, 2);
            """;
        await versionCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask ValidateVersionOneSchemaAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await ValidateColumnsAsync(
                connection,
                transaction,
                DeliveryTableName,
                VersionOneDeliveryColumns,
                cancellationToken)
            .ConfigureAwait(false);
        await ValidateIndexAsync(
                connection,
                transaction,
                EligibilityIndexName,
                [
                    new("state", IsDescending: false),
                    new("next_attempt_utc_ticks", IsDescending: false),
                    new("application_address", IsDescending: false),
                    new("message_id", IsDescending: false)
                ],
                requiresDeadLetterPredicate: false,
                cancellationToken)
            .ConfigureAwait(false);
        if (await ObjectExistsAsync(
                connection,
                transaction,
                "table",
                MigrationTableName,
                cancellationToken)
            .ConfigureAwait(false) ||
            await ObjectExistsAsync(
                connection,
                transaction,
                "index",
                DeadLetterIndexName,
                cancellationToken)
            .ConfigureAwait(false))
        {
            throw new InvalidDataException(
                "SQL-file durable output delivery version-1 schema contains partial version-2 objects.");
        }

        await ValidateVersionOneRowsAsync(connection, transaction, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async ValueTask MigrateVersionOneAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await CreateVersionTwoDeliveryTableAsync(
                connection,
                transaction,
                MigrationTableName,
                cancellationToken)
            .ConfigureAwait(false);

        await using (var copyCommand = connection.CreateCommand())
        {
            copyCommand.Transaction = transaction;
            copyCommand.CommandText = """
                INSERT INTO fluxflow_durable_output_deliveries_v2 (
                    application_address,
                    message_id,
                    state,
                    next_attempt_utc_ticks,
                    next_attempt_offset_minutes,
                    lease_token,
                    lease_owner,
                    leased_at_utc_ticks,
                    leased_at_offset_minutes,
                    lease_until_utc_ticks,
                    lease_until_offset_minutes,
                    attempt,
                    delivered_at_utc_ticks,
                    delivered_at_offset_minutes,
                    dead_letter_reason,
                    dead_lettered_at_utc_ticks,
                    dead_lettered_at_offset_minutes,
                    dead_letter_generation)
                SELECT application_address,
                       message_id,
                       state,
                       next_attempt_utc_ticks,
                       next_attempt_offset_minutes,
                       lease_token,
                       lease_owner,
                       leased_at_utc_ticks,
                       leased_at_offset_minutes,
                       lease_until_utc_ticks,
                       lease_until_offset_minutes,
                       attempt,
                       delivered_at_utc_ticks,
                       delivered_at_offset_minutes,
                       NULL,
                       NULL,
                       NULL,
                       0
                FROM fluxflow_durable_output_deliveries;

                DROP TABLE fluxflow_durable_output_deliveries;
                ALTER TABLE fluxflow_durable_output_deliveries_v2
                    RENAME TO fluxflow_durable_output_deliveries;
                """;
            await copyCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await CreateIndexesAsync(connection, transaction, cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        await using var versionCommand = connection.CreateCommand();
        versionCommand.Transaction = transaction;
        versionCommand.CommandText = """
            UPDATE fluxflow_durable_output_delivery_schema
            SET version = 2
            WHERE singleton = 1 AND version = 1;
            """;
        var affected = await versionCommand.ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false);
        if (affected != 1)
        {
            throw new InvalidDataException(
                "SQL-file durable output delivery schema migration did not update its version.");
        }
    }

    private static async ValueTask CreateVersionTwoDeliveryTableAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string tableName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            CREATE TABLE {tableName} (
                application_address TEXT COLLATE BINARY NOT NULL,
                message_id TEXT COLLATE BINARY NOT NULL,
                state INTEGER NOT NULL CHECK (state IN (1, 2, 3, 4)),
                next_attempt_utc_ticks INTEGER NOT NULL,
                next_attempt_offset_minutes INTEGER NOT NULL
                    CHECK (next_attempt_offset_minutes BETWEEN -840 AND 840),
                lease_token TEXT COLLATE BINARY NULL,
                lease_owner TEXT COLLATE BINARY NULL,
                leased_at_utc_ticks INTEGER NULL,
                leased_at_offset_minutes INTEGER NULL
                    CHECK (leased_at_offset_minutes IS NULL
                        OR leased_at_offset_minutes BETWEEN -840 AND 840),
                lease_until_utc_ticks INTEGER NULL,
                lease_until_offset_minutes INTEGER NULL
                    CHECK (lease_until_offset_minutes IS NULL
                        OR lease_until_offset_minutes BETWEEN -840 AND 840),
                attempt INTEGER NOT NULL CHECK (attempt >= 0),
                delivered_at_utc_ticks INTEGER NULL,
                delivered_at_offset_minutes INTEGER NULL
                    CHECK (delivered_at_offset_minutes IS NULL
                        OR delivered_at_offset_minutes BETWEEN -840 AND 840),
                dead_letter_reason INTEGER NULL
                    CHECK (dead_letter_reason IS NULL OR dead_letter_reason = 1),
                dead_lettered_at_utc_ticks INTEGER NULL,
                dead_lettered_at_offset_minutes INTEGER NULL
                    CHECK (dead_lettered_at_offset_minutes IS NULL
                        OR dead_lettered_at_offset_minutes BETWEEN -840 AND 840),
                dead_letter_generation INTEGER NOT NULL DEFAULT 0
                    CHECK (dead_letter_generation >= 0),
                PRIMARY KEY (application_address, message_id),
                FOREIGN KEY (application_address, message_id)
                    REFERENCES fluxflow_durable_outputs (application_address, message_id)
                    ON DELETE CASCADE,
                CHECK (
                    (state = 1
                        AND lease_token IS NULL
                        AND lease_owner IS NULL
                        AND leased_at_utc_ticks IS NULL
                        AND leased_at_offset_minutes IS NULL
                        AND lease_until_utc_ticks IS NULL
                        AND lease_until_offset_minutes IS NULL
                        AND delivered_at_utc_ticks IS NULL
                        AND delivered_at_offset_minutes IS NULL
                        AND dead_letter_reason IS NULL
                        AND dead_lettered_at_utc_ticks IS NULL
                        AND dead_lettered_at_offset_minutes IS NULL)
                    OR (state = 2
                        AND lease_token IS NOT NULL
                        AND length(lease_token) = 32
                        AND lease_owner IS NOT NULL
                        AND length(lease_owner) > 0
                        AND leased_at_utc_ticks IS NOT NULL
                        AND leased_at_offset_minutes IS NOT NULL
                        AND lease_until_utc_ticks IS NOT NULL
                        AND lease_until_offset_minutes IS NOT NULL
                        AND lease_until_utc_ticks > leased_at_utc_ticks
                        AND attempt > 0
                        AND delivered_at_utc_ticks IS NULL
                        AND delivered_at_offset_minutes IS NULL
                        AND dead_letter_reason IS NULL
                        AND dead_lettered_at_utc_ticks IS NULL
                        AND dead_lettered_at_offset_minutes IS NULL)
                    OR (state = 3
                        AND lease_token IS NULL
                        AND lease_owner IS NULL
                        AND leased_at_utc_ticks IS NULL
                        AND leased_at_offset_minutes IS NULL
                        AND lease_until_utc_ticks IS NULL
                        AND lease_until_offset_minutes IS NULL
                        AND attempt > 0
                        AND delivered_at_utc_ticks IS NOT NULL
                        AND delivered_at_offset_minutes IS NOT NULL
                        AND dead_letter_reason IS NULL
                        AND dead_lettered_at_utc_ticks IS NULL
                        AND dead_lettered_at_offset_minutes IS NULL)
                    OR (state = 4
                        AND lease_token IS NULL
                        AND lease_owner IS NULL
                        AND leased_at_utc_ticks IS NULL
                        AND leased_at_offset_minutes IS NULL
                        AND lease_until_utc_ticks IS NULL
                        AND lease_until_offset_minutes IS NULL
                        AND attempt > 0
                        AND delivered_at_utc_ticks IS NULL
                        AND delivered_at_offset_minutes IS NULL
                        AND dead_letter_reason = 1
                        AND dead_lettered_at_utc_ticks IS NOT NULL
                        AND dead_lettered_at_offset_minutes IS NOT NULL
                        AND dead_letter_generation > 0)
                )
            ) WITHOUT ROWID;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask CreateIndexesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE INDEX ix_fluxflow_durable_output_deliveries_eligibility
            ON fluxflow_durable_output_deliveries (
                state,
                next_attempt_utc_ticks,
                application_address,
                message_id
            );

            CREATE INDEX ix_fluxflow_durable_output_deliveries_dead_lettered
            ON fluxflow_durable_output_deliveries (
                state,
                dead_lettered_at_utc_ticks DESC,
                application_address,
                message_id
            )
            WHERE state = 4;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask ValidateCurrentSchemaAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        if (await ObjectExistsAsync(
                connection,
                transaction,
                "table",
                MigrationTableName,
                cancellationToken)
            .ConfigureAwait(false))
        {
            throw new InvalidDataException(
                "SQL-file durable output delivery schema contains a partial migration table.");
        }

        await ValidateColumnsAsync(
                connection,
                transaction,
                DeliveryTableName,
                DeliveryColumns,
                cancellationToken)
            .ConfigureAwait(false);
        await ValidateIndexAsync(
                connection,
                transaction,
                EligibilityIndexName,
                [
                    new("state", IsDescending: false),
                    new("next_attempt_utc_ticks", IsDescending: false),
                    new("application_address", IsDescending: false),
                    new("message_id", IsDescending: false)
                ],
                requiresDeadLetterPredicate: false,
                cancellationToken)
            .ConfigureAwait(false);
        await ValidateIndexAsync(
                connection,
                transaction,
                DeadLetterIndexName,
                [
                    new("state", IsDescending: false),
                    new("dead_lettered_at_utc_ticks", IsDescending: true),
                    new("application_address", IsDescending: false),
                    new("message_id", IsDescending: false)
                ],
                requiresDeadLetterPredicate: true,
                cancellationToken)
            .ConfigureAwait(false);
        await ValidateRowsAsync(connection, transaction, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async ValueTask<int> ReadVersionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT singleton, version FROM fluxflow_durable_output_delivery_schema;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            throw new InvalidDataException("SQL-file durable output delivery schema version is missing.");

        var singleton = reader.GetInt32(0);
        var version = reader.GetInt32(1);
        if (singleton != 1 || version <= 0)
        {
            throw new InvalidDataException(
                "SQL-file durable output delivery schema version metadata is invalid.");
        }

        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidDataException(
                "SQL-file durable output delivery schema contains multiple version rows.");
        }

        return version;
    }

    private static async ValueTask ValidateVersionOneRowsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT COUNT(*)
            FROM fluxflow_durable_output_deliveries AS d
            LEFT JOIN fluxflow_durable_outputs AS o
              ON o.application_address = d.application_address
             AND o.message_id = d.message_id
            WHERE o.application_address IS NULL
               OR d.state NOT IN (1, 2, 3)
               OR d.attempt < 0
               OR d.next_attempt_offset_minutes NOT BETWEEN -840 AND 840
               OR (d.state = 1 AND (
                    d.lease_token IS NOT NULL OR d.lease_owner IS NOT NULL
                    OR d.leased_at_utc_ticks IS NOT NULL
                    OR d.leased_at_offset_minutes IS NOT NULL
                    OR d.lease_until_utc_ticks IS NOT NULL
                    OR d.lease_until_offset_minutes IS NOT NULL
                    OR d.delivered_at_utc_ticks IS NOT NULL
                    OR d.delivered_at_offset_minutes IS NOT NULL))
               OR (d.state = 2 AND (
                    d.lease_token IS NULL OR length(d.lease_token) <> 32
                    OR d.lease_owner IS NULL OR length(d.lease_owner) = 0
                    OR d.leased_at_utc_ticks IS NULL
                    OR d.leased_at_offset_minutes IS NULL
                    OR d.lease_until_utc_ticks IS NULL
                    OR d.lease_until_offset_minutes IS NULL
                    OR d.lease_until_utc_ticks <= d.leased_at_utc_ticks
                    OR d.attempt <= 0
                    OR d.delivered_at_utc_ticks IS NOT NULL
                    OR d.delivered_at_offset_minutes IS NOT NULL))
               OR (d.state = 3 AND (
                    d.lease_token IS NOT NULL OR d.lease_owner IS NOT NULL
                    OR d.leased_at_utc_ticks IS NOT NULL
                    OR d.leased_at_offset_minutes IS NOT NULL
                    OR d.lease_until_utc_ticks IS NOT NULL
                    OR d.lease_until_offset_minutes IS NOT NULL
                    OR d.attempt <= 0
                    OR d.delivered_at_utc_ticks IS NULL
                    OR d.delivered_at_offset_minutes IS NULL));
            """;
        await ThrowIfInvalidRowsAsync(command, cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask ValidateRowsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT COUNT(*)
            FROM fluxflow_durable_output_deliveries AS d
            LEFT JOIN fluxflow_durable_outputs AS o
              ON o.application_address = d.application_address
             AND o.message_id = d.message_id
            WHERE o.application_address IS NULL
               OR d.state NOT IN (1, 2, 3, 4)
               OR d.attempt < 0
               OR d.dead_letter_generation < 0
               OR d.next_attempt_offset_minutes NOT BETWEEN -840 AND 840
               OR (d.state = 1 AND (
                    d.lease_token IS NOT NULL OR d.lease_owner IS NOT NULL
                    OR d.leased_at_utc_ticks IS NOT NULL
                    OR d.leased_at_offset_minutes IS NOT NULL
                    OR d.lease_until_utc_ticks IS NOT NULL
                    OR d.lease_until_offset_minutes IS NOT NULL
                    OR d.delivered_at_utc_ticks IS NOT NULL
                    OR d.delivered_at_offset_minutes IS NOT NULL
                    OR d.dead_letter_reason IS NOT NULL
                    OR d.dead_lettered_at_utc_ticks IS NOT NULL
                    OR d.dead_lettered_at_offset_minutes IS NOT NULL))
               OR (d.state = 2 AND (
                    d.lease_token IS NULL OR length(d.lease_token) <> 32
                    OR d.lease_owner IS NULL OR length(d.lease_owner) = 0
                    OR d.leased_at_utc_ticks IS NULL
                    OR d.leased_at_offset_minutes IS NULL
                    OR d.lease_until_utc_ticks IS NULL
                    OR d.lease_until_offset_minutes IS NULL
                    OR d.lease_until_utc_ticks <= d.leased_at_utc_ticks
                    OR d.attempt <= 0
                    OR d.delivered_at_utc_ticks IS NOT NULL
                    OR d.delivered_at_offset_minutes IS NOT NULL
                    OR d.dead_letter_reason IS NOT NULL
                    OR d.dead_lettered_at_utc_ticks IS NOT NULL
                    OR d.dead_lettered_at_offset_minutes IS NOT NULL))
               OR (d.state = 3 AND (
                    d.lease_token IS NOT NULL OR d.lease_owner IS NOT NULL
                    OR d.leased_at_utc_ticks IS NOT NULL
                    OR d.leased_at_offset_minutes IS NOT NULL
                    OR d.lease_until_utc_ticks IS NOT NULL
                    OR d.lease_until_offset_minutes IS NOT NULL
                    OR d.attempt <= 0
                    OR d.delivered_at_utc_ticks IS NULL
                    OR d.delivered_at_offset_minutes IS NULL
                    OR d.dead_letter_reason IS NOT NULL
                    OR d.dead_lettered_at_utc_ticks IS NOT NULL
                    OR d.dead_lettered_at_offset_minutes IS NOT NULL))
               OR (d.state = 4 AND (
                    d.lease_token IS NOT NULL OR d.lease_owner IS NOT NULL
                    OR d.leased_at_utc_ticks IS NOT NULL
                    OR d.leased_at_offset_minutes IS NOT NULL
                    OR d.lease_until_utc_ticks IS NOT NULL
                    OR d.lease_until_offset_minutes IS NOT NULL
                    OR d.attempt <= 0
                    OR d.delivered_at_utc_ticks IS NOT NULL
                    OR d.delivered_at_offset_minutes IS NOT NULL
                    OR d.dead_letter_reason IS NULL
                    OR d.dead_letter_reason <> 1
                    OR d.dead_lettered_at_utc_ticks IS NULL
                    OR d.dead_lettered_at_offset_minutes IS NULL
                    OR d.dead_letter_generation <= 0));
            """;
        await ThrowIfInvalidRowsAsync(command, cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask ThrowIfInvalidRowsAsync(
        SqliteCommand command,
        CancellationToken cancellationToken)
    {
        var invalidCount = Convert.ToInt64(
            await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
            CultureInfo.InvariantCulture);
        if (invalidCount != 0)
        {
            throw new InvalidDataException(
                "SQL-file durable output delivery schema contains corrupt state rows.");
        }
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
                $"SQL-file durable output delivery schema is missing required table '{table}'.");
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
                reader.GetInt32(5),
                reader.IsDBNull(4) ? null : reader.GetString(4)));
        }

        if (actual.Count != expected.Count)
        {
            throw new InvalidDataException(
                $"SQL-file durable output delivery table '{table}' has an incompatible column count.");
        }

        for (var index = 0; index < expected.Count; index++)
        {
            var expectedColumn = expected[index];
            var actualColumn = actual[index];
            if (!string.Equals(actualColumn.Name, expectedColumn.Name, StringComparison.Ordinal) ||
                !string.Equals(actualColumn.Type, expectedColumn.Type, StringComparison.OrdinalIgnoreCase) ||
                actualColumn.IsNotNull != expectedColumn.IsNotNull ||
                actualColumn.PrimaryKeyOrdinal != expectedColumn.PrimaryKeyOrdinal ||
                !string.Equals(
                    actualColumn.DefaultValue,
                    expectedColumn.DefaultValue,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"SQL-file durable output delivery table '{table}' has an incompatible column at ordinal {index}.");
            }
        }
    }

    private static async ValueTask ValidateIndexAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string name,
        IReadOnlyList<IndexColumnDefinition> expectedColumns,
        bool requiresDeadLetterPredicate,
        CancellationToken cancellationToken)
    {
        if (!await ObjectExistsAsync(
                connection,
                transaction,
                "index",
                name,
                cancellationToken)
            .ConfigureAwait(false))
        {
            throw new InvalidDataException(
                $"SQL-file durable output delivery schema is missing required index '{name}'.");
        }

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = FormattableString.Invariant($"PRAGMA index_xinfo('{name}');");
            var actual = new List<IndexColumnDefinition>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                if (reader.GetInt32(5) == 0)
                    continue;

                actual.Add(new IndexColumnDefinition(
                    reader.GetString(2),
                    reader.GetInt32(3) == 1));
            }

            if (!actual.SequenceEqual(expectedColumns))
            {
                throw new InvalidDataException(
                    $"SQL-file durable output delivery index '{name}' has incompatible columns or ordering.");
            }
        }

        await using var sqlCommand = connection.CreateCommand();
        sqlCommand.Transaction = transaction;
        sqlCommand.CommandText = """
            SELECT sql
            FROM sqlite_schema
            WHERE type = 'index' AND name = $name;
            """;
        sqlCommand.Parameters.AddWithValue("$name", name);
        var sql = Convert.ToString(
            await sqlCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
            CultureInfo.InvariantCulture);
        if (string.IsNullOrWhiteSpace(sql))
        {
            throw new InvalidDataException(
                $"SQL-file durable output delivery index '{name}' has no definition.");
        }

        var normalized = string.Concat(sql.Where(static character => !char.IsWhiteSpace(character)))
            .TrimEnd(';');
        var hasAnyPredicate = normalized.Contains("WHERE", StringComparison.OrdinalIgnoreCase);
        var hasDeadLetterPredicate = normalized.EndsWith(
            "WHEREstate=4",
            StringComparison.OrdinalIgnoreCase);
        if ((requiresDeadLetterPredicate && !hasDeadLetterPredicate) ||
            (!requiresDeadLetterPredicate && hasAnyPredicate))
        {
            throw new InvalidDataException(
                $"SQL-file durable output delivery index '{name}' has an incompatible predicate.");
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
        int PrimaryKeyOrdinal,
        string? DefaultValue = null);

    private sealed record IndexColumnDefinition(
        string Name,
        bool IsDescending);
}
