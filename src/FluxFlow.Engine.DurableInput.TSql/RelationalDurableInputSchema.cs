using System.Data;
using Microsoft.Data.SqlClient;

namespace FluxFlow.Engine.DurableInput.TSql;

internal static class RelationalDurableInputSchema
{
    internal const int CurrentVersion = 1;
    internal const string SchemaTable = "fluxflow_relational_input_schema";
    internal const string InputTable = "fluxflow_relational_inputs";
    internal const string EligibilityIndex = "ix_fluxflow_relational_inputs_eligibility";
    internal const string DeadLetterIndex = "ix_fluxflow_relational_inputs_dead_lettered";

    private const string ApplicationLock = "FluxFlow.RelationalDurableInput.Schema";

    private static readonly ColumnExpectation[] ExpectedSchemaColumns =
    [
        new("singleton", "bit", 1, false),
        new("version", "int", 4, false)
    ];

    private static readonly ColumnExpectation[] ExpectedInputColumns =
    [
        new("application_address", "nvarchar", 600, false),
        new("message_id", "nvarchar", 256, false),
        new("contract_name", "nvarchar", 2048, false),
        new("envelope_schema_version", "int", 4, false),
        new("is_error", "bit", 1, false),
        new("payload_json", "nvarchar", -1, false),
        new("error_code", "nvarchar", 2048, true),
        new("error_message", "nvarchar", -1, true),
        new("error_category", "nvarchar", 2048, true),
        new("error_is_transient", "bit", 1, true),
        new("error_details_json", "nvarchar", -1, true),
        new("trace_id", "nvarchar", 1024, false),
        new("correlation_id", "nvarchar", 1024, true),
        new("causation_id", "nvarchar", 1024, true),
        new("message_timestamp_utc_ticks", "bigint", 8, false),
        new("message_timestamp_offset_minutes", "smallint", 2, false),
        new("enqueued_at_utc_ticks", "bigint", 8, false),
        new("enqueued_at_offset_minutes", "smallint", 2, false),
        new("headers_json", "nvarchar", -1, false),
        new("state", "tinyint", 1, false),
        new("attempt", "int", 4, false),
        new("next_attempt_utc_ticks", "bigint", 8, true),
        new("lease_owner", "nvarchar", 1024, true),
        new("lease_token", "uniqueidentifier", 16, true),
        new("leased_at_utc_ticks", "bigint", 8, true),
        new("lease_until_utc_ticks", "bigint", 8, true),
        new("failure_kind", "int", 4, true),
        new("failure_description", "nvarchar", -1, true),
        new("delivered_at_utc_ticks", "bigint", 8, true),
        new("dead_lettered_at_utc_ticks", "bigint", 8, true),
        new("dead_letter_generation", "bigint", 8, false)
    ];

    internal static async ValueTask InitializeAsync(
        SqlConnection connection,
        TSqlDurableInputStoreSettings settings,
        CancellationToken cancellationToken)
    {
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken)
            .ConfigureAwait(false);

        await AcquireApplicationLockAsync(connection, transaction, settings, cancellationToken)
            .ConfigureAwait(false);
        await ValidateReadCommittedModeAsync(connection, transaction, settings, cancellationToken)
            .ConfigureAwait(false);

        var existing = await ReadOwnedTableCountAsync(
                connection,
                transaction,
                settings,
                cancellationToken)
            .ConfigureAwait(false);
        if (existing == 0)
        {
            if (settings.SchemaManagement == TSqlDurableInputSchemaManagement.ValidateOnly)
            {
                throw new InvalidOperationException(
                    "T-SQL durable-input schema is absent and validate-only mode cannot create it.");
            }

            await ApplyMigrationAsync(
                    connection,
                    transaction,
                    settings,
                    fromVersion: 0,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        else if (existing != 2)
        {
            throw new InvalidDataException(
                "Relational durable-input schema is partial and cannot be repaired automatically.");
        }

        await ValidateAsync(connection, transaction, settings, cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private static async ValueTask AcquireApplicationLockAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        TSqlDurableInputStoreSettings settings,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, transaction, settings);
        command.CommandText = """
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock
                @Resource = @resource,
                @LockMode = 'Exclusive',
                @LockOwner = 'Transaction',
                @LockTimeout = @lockTimeout;
            SELECT @result;
            """;
        var lockWaitSeconds = checked((int)Math.Ceiling(
            settings.SchemaLockTimeoutMilliseconds / 1000d));
        command.CommandTimeout = Math.Max(
            settings.CommandTimeoutSeconds,
            checked(lockWaitSeconds + 1));
        command.Parameters.Add("@resource", SqlDbType.NVarChar, 255).Value = ApplicationLock;
        command.Parameters.Add("@lockTimeout", SqlDbType.Int).Value =
            settings.SchemaLockTimeoutMilliseconds;
        var result = Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
            System.Globalization.CultureInfo.InvariantCulture);
        if (result < 0)
        {
            throw new InvalidOperationException(
                "Relational durable-input schema initialization could not acquire its database lock.");
        }
    }

    private static async ValueTask ValidateReadCommittedModeAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        TSqlDurableInputStoreSettings settings,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, transaction, settings);
        command.CommandText =
            "SELECT is_read_committed_snapshot_on FROM sys.databases WHERE database_id = DB_ID();";
        var enabled = Convert.ToBoolean(
            await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
            System.Globalization.CultureInfo.InvariantCulture);
        if (enabled)
        {
            throw new InvalidOperationException(
                "T-SQL durable input requires READ_COMMITTED_SNAPSHOT to be disabled for its locking lease strategy.");
        }
    }

    private static async ValueTask<int> ReadOwnedTableCountAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        TSqlDurableInputStoreSettings settings,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, transaction, settings);
        command.CommandText = """
            SELECT COUNT(*)
            FROM sys.tables AS t
            INNER JOIN sys.schemas AS s ON s.schema_id = t.schema_id
            WHERE s.name = N'dbo'
              AND t.name IN (
                  N'fluxflow_relational_input_schema',
                  N'fluxflow_relational_inputs');
            """;
        return Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
            System.Globalization.CultureInfo.InvariantCulture);
    }

    private static ValueTask ApplyMigrationAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        TSqlDurableInputStoreSettings settings,
        int fromVersion,
        CancellationToken cancellationToken)
        => fromVersion switch
        {
            0 => CreateVersionOneAsync(connection, transaction, settings, cancellationToken),
            _ => throw new NotSupportedException(
                $"T-SQL durable-input schema version {fromVersion} has no known migration path.")
        };

    private static async ValueTask CreateVersionOneAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        TSqlDurableInputStoreSettings settings,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, transaction, settings);
        command.CommandText = """
            CREATE TABLE dbo.fluxflow_relational_input_schema (
                singleton bit NOT NULL
                    CONSTRAINT pk_fluxflow_relational_input_schema PRIMARY KEY,
                version int NOT NULL,
                CONSTRAINT ck_fluxflow_relational_input_schema_singleton CHECK (singleton = 1),
                CONSTRAINT ck_fluxflow_relational_input_schema_version CHECK (version > 0)
            );

            CREATE TABLE dbo.fluxflow_relational_inputs (
                application_address nvarchar(300) COLLATE Latin1_General_100_BIN2 NOT NULL,
                message_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
                contract_name nvarchar(1024) NOT NULL,
                envelope_schema_version int NOT NULL,
                is_error bit NOT NULL,
                payload_json nvarchar(max) NOT NULL,
                error_code nvarchar(1024) NULL,
                error_message nvarchar(max) NULL,
                error_category nvarchar(1024) NULL,
                error_is_transient bit NULL,
                error_details_json nvarchar(max) NULL,
                trace_id nvarchar(512) NOT NULL,
                correlation_id nvarchar(512) NULL,
                causation_id nvarchar(512) NULL,
                message_timestamp_utc_ticks bigint NOT NULL,
                message_timestamp_offset_minutes smallint NOT NULL,
                enqueued_at_utc_ticks bigint NOT NULL,
                enqueued_at_offset_minutes smallint NOT NULL,
                headers_json nvarchar(max) NOT NULL,
                state tinyint NOT NULL,
                attempt int NOT NULL,
                next_attempt_utc_ticks bigint NULL,
                lease_owner nvarchar(512) NULL,
                lease_token uniqueidentifier NULL,
                leased_at_utc_ticks bigint NULL,
                lease_until_utc_ticks bigint NULL,
                failure_kind int NULL,
                failure_description nvarchar(max) NULL,
                delivered_at_utc_ticks bigint NULL,
                dead_lettered_at_utc_ticks bigint NULL,
                dead_letter_generation bigint NOT NULL,
                CONSTRAINT pk_fluxflow_relational_inputs
                    PRIMARY KEY (application_address, message_id),
                CONSTRAINT ck_fluxflow_relational_inputs_contract
                    CHECK (LEN(contract_name) > 0),
                CONSTRAINT ck_fluxflow_relational_inputs_envelope_version
                    CHECK (envelope_schema_version > 0),
                CONSTRAINT ck_fluxflow_relational_inputs_error_shape CHECK (
                    (is_error = 0
                        AND error_code IS NULL AND error_message IS NULL
                        AND error_category IS NULL AND error_is_transient IS NULL
                        AND error_details_json IS NULL)
                    OR (is_error = 1
                        AND error_code IS NOT NULL AND error_message IS NOT NULL
                        AND error_category IS NOT NULL AND error_is_transient IS NOT NULL)),
                CONSTRAINT ck_fluxflow_relational_inputs_message_offset
                    CHECK (message_timestamp_offset_minutes BETWEEN -840 AND 840),
                CONSTRAINT ck_fluxflow_relational_inputs_enqueue_offset
                    CHECK (enqueued_at_offset_minutes BETWEEN -840 AND 840),
                CONSTRAINT ck_fluxflow_relational_inputs_state
                    CHECK (state IN (0, 1, 2, 3)),
                CONSTRAINT ck_fluxflow_relational_inputs_attempt
                    CHECK (attempt >= 0),
                CONSTRAINT ck_fluxflow_relational_inputs_failure_shape CHECK (
                    (failure_kind IS NULL AND failure_description IS NULL)
                    OR (failure_kind IS NOT NULL AND failure_description IS NOT NULL)),
                CONSTRAINT ck_fluxflow_relational_inputs_generation
                    CHECK (dead_letter_generation >= 0),
                CONSTRAINT ck_fluxflow_relational_inputs_operational_shape CHECK (
                    (state = 0
                        AND next_attempt_utc_ticks IS NOT NULL
                        AND lease_owner IS NULL AND lease_token IS NULL
                        AND leased_at_utc_ticks IS NULL AND lease_until_utc_ticks IS NULL
                        AND delivered_at_utc_ticks IS NULL
                        AND dead_lettered_at_utc_ticks IS NULL)
                    OR (state = 1
                        AND attempt > 0 AND next_attempt_utc_ticks IS NULL
                        AND lease_owner IS NOT NULL AND LEN(lease_owner) > 0
                        AND lease_token IS NOT NULL
                        AND leased_at_utc_ticks IS NOT NULL
                        AND lease_until_utc_ticks IS NOT NULL
                        AND lease_until_utc_ticks > leased_at_utc_ticks
                        AND delivered_at_utc_ticks IS NULL
                        AND dead_lettered_at_utc_ticks IS NULL)
                    OR (state = 2
                        AND attempt > 0 AND next_attempt_utc_ticks IS NULL
                        AND lease_owner IS NULL AND lease_token IS NULL
                        AND leased_at_utc_ticks IS NULL AND lease_until_utc_ticks IS NULL
                        AND delivered_at_utc_ticks IS NOT NULL
                        AND dead_lettered_at_utc_ticks IS NULL)
                    OR (state = 3
                        AND attempt > 0 AND next_attempt_utc_ticks IS NULL
                        AND lease_owner IS NULL AND lease_token IS NULL
                        AND leased_at_utc_ticks IS NULL AND lease_until_utc_ticks IS NULL
                        AND failure_kind IS NOT NULL
                        AND delivered_at_utc_ticks IS NULL
                        AND dead_lettered_at_utc_ticks IS NOT NULL
                        AND dead_letter_generation > 0))
            );

            CREATE INDEX ix_fluxflow_relational_inputs_eligibility
                ON dbo.fluxflow_relational_inputs (
                    state,
                    next_attempt_utc_ticks,
                    lease_until_utc_ticks,
                    enqueued_at_utc_ticks,
                    application_address,
                    message_id);

            CREATE INDEX ix_fluxflow_relational_inputs_dead_lettered
                ON dbo.fluxflow_relational_inputs (
                    state,
                    dead_lettered_at_utc_ticks DESC,
                    application_address,
                    message_id)
                WHERE state = 3;

            INSERT INTO dbo.fluxflow_relational_input_schema (singleton, version)
            VALUES (1, 1);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask ValidateAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        TSqlDurableInputStoreSettings settings,
        CancellationToken cancellationToken)
    {
        var version = await ReadVersionAsync(connection, transaction, settings, cancellationToken)
            .ConfigureAwait(false);
        if (version > CurrentVersion)
        {
            throw new NotSupportedException(
                $"Relational durable-input schema version {version} is newer than supported version {CurrentVersion}.");
        }

        if (version < CurrentVersion)
        {
            throw new NotSupportedException(
                $"Relational durable-input schema version {version} cannot be migrated to version {CurrentVersion}.");
        }

        await ValidateColumnsAsync(
                connection,
                transaction,
                settings,
                SchemaTable,
                ExpectedSchemaColumns,
                cancellationToken)
            .ConfigureAwait(false);
        await ValidateColumnsAsync(
                connection,
                transaction,
                settings,
                InputTable,
                ExpectedInputColumns,
                cancellationToken)
            .ConfigureAwait(false);
        await ValidateBinaryKeyCollationAsync(connection, transaction, settings, cancellationToken)
            .ConfigureAwait(false);
        await ValidatePrimaryKeyAsync(
                connection,
                transaction,
                settings,
                SchemaTable,
                ["singleton"],
                cancellationToken)
            .ConfigureAwait(false);
        await ValidatePrimaryKeyAsync(
                connection,
                transaction,
                settings,
                InputTable,
                ["application_address", "message_id"],
                cancellationToken)
            .ConfigureAwait(false);
        await ValidateCheckConstraintsAsync(
                connection,
                transaction,
                settings,
                SchemaTable,
                2,
                cancellationToken)
            .ConfigureAwait(false);
        await ValidateCheckConstraintsAsync(
                connection,
                transaction,
                settings,
                InputTable,
                10,
                cancellationToken)
            .ConfigureAwait(false);
        await ValidateIndexAsync(
                connection,
                transaction,
                settings,
                EligibilityIndex,
                [
                    "state:0",
                    "next_attempt_utc_ticks:0",
                    "lease_until_utc_ticks:0",
                    "enqueued_at_utc_ticks:0",
                    "application_address:0",
                    "message_id:0"
                ],
                isFiltered: false,
                cancellationToken)
            .ConfigureAwait(false);
        await ValidateIndexAsync(
                connection,
                transaction,
                settings,
                DeadLetterIndex,
                [
                    "state:0",
                    "dead_lettered_at_utc_ticks:1",
                    "application_address:0",
                    "message_id:0"
                ],
                isFiltered: true,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async ValueTask<int> ReadVersionAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        TSqlDurableInputStoreSettings settings,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, transaction, settings);
        command.CommandText =
            "SELECT CAST(singleton AS int), version FROM dbo.fluxflow_relational_input_schema;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            throw new InvalidDataException("Relational durable-input schema version is missing.");

        var singleton = reader.GetInt32(0);
        var version = reader.GetInt32(1);
        if (singleton != 1 || version <= 0)
            throw new InvalidDataException("Relational durable-input schema version metadata is invalid.");
        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            throw new InvalidDataException("Relational durable-input schema contains multiple version rows.");
        return version;
    }

    private static async ValueTask ValidateColumnsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        TSqlDurableInputStoreSettings settings,
        string table,
        IReadOnlyList<ColumnExpectation> expected,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, transaction, settings);
        command.CommandText = """
            SELECT c.name, ty.name, c.max_length, c.is_nullable
            FROM sys.columns AS c
            INNER JOIN sys.tables AS t ON t.object_id = c.object_id
            INNER JOIN sys.schemas AS s ON s.schema_id = t.schema_id
            INNER JOIN sys.types AS ty ON ty.user_type_id = c.user_type_id
            WHERE s.name = N'dbo' AND t.name = @table
            ORDER BY c.column_id;
            """;
        command.Parameters.Add("@table", SqlDbType.NVarChar, 128).Value = table;
        var actual = new List<ColumnExpectation>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            actual.Add(new ColumnExpectation(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetInt16(2),
                reader.GetBoolean(3)));
        }

        if (!actual.SequenceEqual(expected))
        {
            throw new InvalidDataException(
                $"Relational durable-input table 'dbo.{table}' has incompatible columns.");
        }
    }

    private static async ValueTask ValidateBinaryKeyCollationAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        TSqlDurableInputStoreSettings settings,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, transaction, settings);
        command.CommandText = """
            SELECT COUNT(*)
            FROM sys.columns AS c
            INNER JOIN sys.tables AS t ON t.object_id = c.object_id
            INNER JOIN sys.schemas AS s ON s.schema_id = t.schema_id
            WHERE s.name = N'dbo' AND t.name = N'fluxflow_relational_inputs'
              AND c.name IN (N'application_address', N'message_id')
              AND c.collation_name = N'Latin1_General_100_BIN2';
            """;
        var count = Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
            System.Globalization.CultureInfo.InvariantCulture);
        if (count != 2)
        {
            throw new InvalidDataException(
                "Relational durable-input table 'dbo.fluxflow_relational_inputs' has incompatible key collation.");
        }
    }

    private static async ValueTask ValidatePrimaryKeyAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        TSqlDurableInputStoreSettings settings,
        string table,
        IReadOnlyList<string> expectedColumns,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, transaction, settings);
        command.CommandText = """
            SELECT c.name
            FROM sys.indexes AS i
            INNER JOIN sys.tables AS t ON t.object_id = i.object_id
            INNER JOIN sys.schemas AS s ON s.schema_id = t.schema_id
            INNER JOIN sys.index_columns AS ic
              ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            INNER JOIN sys.columns AS c
              ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE s.name = N'dbo' AND t.name = @table AND i.is_primary_key = 1
            ORDER BY ic.key_ordinal;
            """;
        command.Parameters.Add("@table", SqlDbType.NVarChar, 128).Value = table;
        var actual = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            actual.Add(reader.GetString(0));
        if (!actual.SequenceEqual(expectedColumns, StringComparer.Ordinal))
        {
            throw new InvalidDataException(
                $"Relational durable-input table 'dbo.{table}' has an incompatible primary key.");
        }
    }

    private static async ValueTask ValidateCheckConstraintsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        TSqlDurableInputStoreSettings settings,
        string table,
        int expectedCount,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, transaction, settings);
        command.CommandText = """
            SELECT COUNT(*)
            FROM sys.check_constraints AS cc
            INNER JOIN sys.tables AS t ON t.object_id = cc.parent_object_id
            INNER JOIN sys.schemas AS s ON s.schema_id = t.schema_id
            WHERE s.name = N'dbo' AND t.name = @table
              AND cc.is_disabled = 0 AND cc.is_not_trusted = 0;
            """;
        command.Parameters.Add("@table", SqlDbType.NVarChar, 128).Value = table;
        var count = Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
            System.Globalization.CultureInfo.InvariantCulture);
        if (count != expectedCount)
        {
            throw new InvalidDataException(
                $"Relational durable-input table 'dbo.{table}' has incompatible check constraints.");
        }
    }

    private static async ValueTask ValidateIndexAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        TSqlDurableInputStoreSettings settings,
        string index,
        IReadOnlyList<string> expected,
        bool isFiltered,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, transaction, settings);
        command.CommandText = """
            SELECT c.name, ic.is_descending_key, i.has_filter
            FROM sys.indexes AS i
            INNER JOIN sys.index_columns AS ic
              ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            INNER JOIN sys.columns AS c
              ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE i.object_id = OBJECT_ID(N'dbo.fluxflow_relational_inputs')
              AND i.name = @index AND ic.is_included_column = 0
            ORDER BY ic.key_ordinal;
            """;
        command.Parameters.Add("@index", SqlDbType.NVarChar, 128).Value = index;
        var actual = new List<string>();
        bool? actualFiltered = null;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            actual.Add($"{reader.GetString(0)}:{(reader.GetBoolean(1) ? 1 : 0)}");
            actualFiltered = reader.GetBoolean(2);
        }

        if (!actual.SequenceEqual(expected, StringComparer.Ordinal) || actualFiltered != isFiltered)
        {
            throw new InvalidDataException(
                $"Relational durable-input index '{index}' is missing or incompatible.");
        }
    }

    private static SqlCommand CreateCommand(
        SqlConnection connection,
        SqlTransaction transaction,
        TSqlDurableInputStoreSettings settings)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = settings.CommandTimeoutSeconds;
        return command;
    }

    private sealed record ColumnExpectation(
        string Name,
        string Type,
        short MaxLength,
        bool IsNullable);
}
