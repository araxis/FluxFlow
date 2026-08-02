using System.Data;
using Microsoft.Data.SqlClient;

namespace FluxFlow.Engine.DurableOutput.TSql;

internal static class RelationalDurableOutputSchema
{
    internal const int CurrentVersion = 1;
    internal const string SchemaTable = "fluxflow_relational_output_schema";
    internal const string OutputTable = "fluxflow_relational_outputs";
    internal const string DeliveryTable = "fluxflow_relational_output_deliveries";
    internal const string EligibilityIndex = "ix_fluxflow_relational_output_deliveries_eligibility";
    internal const string DeadLetterIndex = "ix_fluxflow_relational_output_deliveries_dead_lettered";

    private const string ApplicationLock = "FluxFlow.RelationalDurableOutput.Schema";

    private static readonly ColumnExpectation[] ExpectedSchemaColumns =
    [
        new("singleton", "bit", 1, IsNullable: false),
        new("version", "int", 4, IsNullable: false)
    ];

    private static readonly ColumnExpectation[] ExpectedOutputColumns =
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
        new("captured_at_utc_ticks", "bigint", 8, false),
        new("captured_at_offset_minutes", "smallint", 2, false),
        new("headers_json", "nvarchar", -1, false)
    ];

    private static readonly ColumnExpectation[] ExpectedDeliveryColumns =
    [
        new("application_address", "nvarchar", 600, false),
        new("message_id", "nvarchar", 256, false),
        new("state", "tinyint", 1, false),
        new("next_attempt_utc_ticks", "bigint", 8, false),
        new("next_attempt_offset_minutes", "smallint", 2, false),
        new("lease_token", "uniqueidentifier", 16, true),
        new("lease_owner", "nvarchar", 1024, true),
        new("leased_at_utc_ticks", "bigint", 8, true),
        new("leased_at_offset_minutes", "smallint", 2, true),
        new("lease_until_utc_ticks", "bigint", 8, true),
        new("lease_until_offset_minutes", "smallint", 2, true),
        new("attempt", "int", 4, false),
        new("delivered_at_utc_ticks", "bigint", 8, true),
        new("delivered_at_offset_minutes", "smallint", 2, true),
        new("dead_letter_reason", "int", 4, true),
        new("dead_lettered_at_utc_ticks", "bigint", 8, true),
        new("dead_lettered_at_offset_minutes", "smallint", 2, true),
        new("dead_letter_generation", "bigint", 8, false)
    ];

    internal static async ValueTask InitializeAsync(
        SqlConnection connection,
        TSqlDurableOutputStoreSettings settings,
        CancellationToken cancellationToken)
    {
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken)
            .ConfigureAwait(false);

        await AcquireApplicationLockAsync(connection, transaction, settings, cancellationToken)
            .ConfigureAwait(false);

        await ValidateReadCommittedModeAsync(connection, transaction, cancellationToken)
            .ConfigureAwait(false);

        var existing = await ReadOwnedTableCountAsync(connection, transaction, cancellationToken)
            .ConfigureAwait(false);
        if (existing == 0)
        {
            if (settings.SchemaManagement == TSqlDurableOutputSchemaManagement.ValidateOnly)
            {
                throw new InvalidOperationException(
                    "T-SQL durable-output schema is absent and validate-only mode cannot create it.");
            }

            await ApplyMigrationAsync(connection, transaction, fromVersion: 0, cancellationToken)
                .ConfigureAwait(false);
        }
        else if (existing != 3)
        {
            throw new InvalidDataException(
                "Relational durable-output schema is partial and cannot be repaired automatically.");
        }

        await ValidateAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private static async ValueTask AcquireApplicationLockAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        TSqlDurableOutputStoreSettings settings,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
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
                "Relational durable-output schema initialization could not acquire its database lock.");
        }
    }

    private static async ValueTask ValidateReadCommittedModeAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT is_read_committed_snapshot_on FROM sys.databases WHERE database_id = DB_ID();";
        var enabled = Convert.ToBoolean(
            await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
            System.Globalization.CultureInfo.InvariantCulture);
        if (enabled)
        {
            throw new InvalidOperationException(
                "T-SQL durable output requires READ_COMMITTED_SNAPSHOT to be disabled for its locking lease strategy.");
        }
    }

    private static async ValueTask<int> ReadOwnedTableCountAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT COUNT(*)
            FROM sys.tables AS t
            INNER JOIN sys.schemas AS s ON s.schema_id = t.schema_id
            WHERE s.name = N'dbo'
              AND t.name IN (
                  N'fluxflow_relational_output_schema',
                  N'fluxflow_relational_outputs',
                  N'fluxflow_relational_output_deliveries');
            """;
        return Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
            System.Globalization.CultureInfo.InvariantCulture);
    }

    private static ValueTask ApplyMigrationAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int fromVersion,
        CancellationToken cancellationToken)
        => fromVersion switch
        {
            0 => CreateVersionOneAsync(connection, transaction, cancellationToken),
            _ => throw new NotSupportedException(
                $"T-SQL durable-output schema version {fromVersion} has no known migration path.")
        };

    private static async ValueTask CreateVersionOneAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE dbo.fluxflow_relational_output_schema (
                singleton bit NOT NULL
                    CONSTRAINT pk_fluxflow_relational_output_schema PRIMARY KEY,
                version int NOT NULL,
                CONSTRAINT ck_fluxflow_relational_output_schema_singleton CHECK (singleton = 1),
                CONSTRAINT ck_fluxflow_relational_output_schema_version CHECK (version > 0)
            );

            CREATE TABLE dbo.fluxflow_relational_outputs (
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
                captured_at_utc_ticks bigint NOT NULL,
                captured_at_offset_minutes smallint NOT NULL,
                headers_json nvarchar(max) NOT NULL,
                CONSTRAINT pk_fluxflow_relational_outputs
                    PRIMARY KEY (application_address, message_id),
                CONSTRAINT ck_fluxflow_relational_outputs_address CHECK (LEN(application_address) > 0),
                CONSTRAINT ck_fluxflow_relational_outputs_message CHECK (LEN(message_id) > 0),
                CONSTRAINT ck_fluxflow_relational_outputs_contract CHECK (LEN(contract_name) > 0),
                CONSTRAINT ck_fluxflow_relational_outputs_version CHECK (envelope_schema_version > 0),
                CONSTRAINT ck_fluxflow_relational_outputs_message_offset
                    CHECK (message_timestamp_offset_minutes BETWEEN -840 AND 840),
                CONSTRAINT ck_fluxflow_relational_outputs_capture_offset
                    CHECK (captured_at_offset_minutes BETWEEN -840 AND 840),
                CONSTRAINT ck_fluxflow_relational_outputs_shape CHECK (
                    (is_error = 0
                        AND error_code IS NULL
                        AND error_message IS NULL
                        AND error_category IS NULL
                        AND error_is_transient IS NULL
                        AND error_details_json IS NULL)
                    OR (is_error = 1
                        AND payload_json = N'null'
                        AND error_code IS NOT NULL
                        AND error_message IS NOT NULL
                        AND error_category IS NOT NULL
                        AND error_is_transient IS NOT NULL))
            );

            CREATE TABLE dbo.fluxflow_relational_output_deliveries (
                application_address nvarchar(300) COLLATE Latin1_General_100_BIN2 NOT NULL,
                message_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
                state tinyint NOT NULL,
                next_attempt_utc_ticks bigint NOT NULL,
                next_attempt_offset_minutes smallint NOT NULL,
                lease_token uniqueidentifier NULL,
                lease_owner nvarchar(512) NULL,
                leased_at_utc_ticks bigint NULL,
                leased_at_offset_minutes smallint NULL,
                lease_until_utc_ticks bigint NULL,
                lease_until_offset_minutes smallint NULL,
                attempt int NOT NULL,
                delivered_at_utc_ticks bigint NULL,
                delivered_at_offset_minutes smallint NULL,
                dead_letter_reason int NULL,
                dead_lettered_at_utc_ticks bigint NULL,
                dead_lettered_at_offset_minutes smallint NULL,
                dead_letter_generation bigint NOT NULL
                    CONSTRAINT df_fluxflow_relational_output_deliveries_generation DEFAULT 0,
                CONSTRAINT pk_fluxflow_relational_output_deliveries
                    PRIMARY KEY (application_address, message_id),
                CONSTRAINT fk_fluxflow_relational_output_deliveries_output
                    FOREIGN KEY (application_address, message_id)
                    REFERENCES dbo.fluxflow_relational_outputs (application_address, message_id)
                    ON DELETE CASCADE,
                CONSTRAINT ck_fluxflow_relational_output_deliveries_state CHECK (state IN (1, 2, 3, 4)),
                CONSTRAINT ck_fluxflow_relational_output_deliveries_next_offset
                    CHECK (next_attempt_offset_minutes BETWEEN -840 AND 840),
                CONSTRAINT ck_fluxflow_relational_output_deliveries_leased_offset
                    CHECK (leased_at_offset_minutes IS NULL OR leased_at_offset_minutes BETWEEN -840 AND 840),
                CONSTRAINT ck_fluxflow_relational_output_deliveries_until_offset
                    CHECK (lease_until_offset_minutes IS NULL OR lease_until_offset_minutes BETWEEN -840 AND 840),
                CONSTRAINT ck_fluxflow_relational_output_deliveries_delivered_offset
                    CHECK (delivered_at_offset_minutes IS NULL OR delivered_at_offset_minutes BETWEEN -840 AND 840),
                CONSTRAINT ck_fluxflow_relational_output_deliveries_dead_offset
                    CHECK (dead_lettered_at_offset_minutes IS NULL OR dead_lettered_at_offset_minutes BETWEEN -840 AND 840),
                CONSTRAINT ck_fluxflow_relational_output_deliveries_attempt CHECK (attempt >= 0),
                CONSTRAINT ck_fluxflow_relational_output_deliveries_generation CHECK (dead_letter_generation >= 0),
                CONSTRAINT ck_fluxflow_relational_output_deliveries_shape CHECK (
                    (state = 1
                        AND lease_token IS NULL AND lease_owner IS NULL
                        AND leased_at_utc_ticks IS NULL AND leased_at_offset_minutes IS NULL
                        AND lease_until_utc_ticks IS NULL AND lease_until_offset_minutes IS NULL
                        AND delivered_at_utc_ticks IS NULL AND delivered_at_offset_minutes IS NULL
                        AND dead_letter_reason IS NULL
                        AND dead_lettered_at_utc_ticks IS NULL AND dead_lettered_at_offset_minutes IS NULL)
                    OR (state = 2
                        AND lease_token IS NOT NULL AND lease_owner IS NOT NULL AND LEN(lease_owner) > 0
                        AND leased_at_utc_ticks IS NOT NULL AND leased_at_offset_minutes IS NOT NULL
                        AND lease_until_utc_ticks IS NOT NULL AND lease_until_offset_minutes IS NOT NULL
                        AND lease_until_utc_ticks > leased_at_utc_ticks AND attempt > 0
                        AND delivered_at_utc_ticks IS NULL AND delivered_at_offset_minutes IS NULL
                        AND dead_letter_reason IS NULL
                        AND dead_lettered_at_utc_ticks IS NULL AND dead_lettered_at_offset_minutes IS NULL)
                    OR (state = 3
                        AND lease_token IS NULL AND lease_owner IS NULL
                        AND leased_at_utc_ticks IS NULL AND leased_at_offset_minutes IS NULL
                        AND lease_until_utc_ticks IS NULL AND lease_until_offset_minutes IS NULL
                        AND attempt > 0
                        AND delivered_at_utc_ticks IS NOT NULL AND delivered_at_offset_minutes IS NOT NULL
                        AND dead_letter_reason IS NULL
                        AND dead_lettered_at_utc_ticks IS NULL AND dead_lettered_at_offset_minutes IS NULL)
                    OR (state = 4
                        AND lease_token IS NULL AND lease_owner IS NULL
                        AND leased_at_utc_ticks IS NULL AND leased_at_offset_minutes IS NULL
                        AND lease_until_utc_ticks IS NULL AND lease_until_offset_minutes IS NULL
                        AND attempt > 0
                        AND delivered_at_utc_ticks IS NULL AND delivered_at_offset_minutes IS NULL
                        AND dead_letter_reason = 1
                        AND dead_lettered_at_utc_ticks IS NOT NULL AND dead_lettered_at_offset_minutes IS NOT NULL
                        AND dead_letter_generation > 0))
            );

            CREATE INDEX ix_fluxflow_relational_output_deliveries_eligibility
                ON dbo.fluxflow_relational_output_deliveries (
                    state,
                    next_attempt_utc_ticks,
                    lease_until_utc_ticks,
                    application_address,
                    message_id);

            CREATE INDEX ix_fluxflow_relational_output_deliveries_dead_lettered
                ON dbo.fluxflow_relational_output_deliveries (
                    state,
                    dead_lettered_at_utc_ticks DESC,
                    application_address,
                    message_id)
                WHERE state = 4;

            INSERT INTO dbo.fluxflow_relational_output_schema (singleton, version)
            VALUES (1, 1);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask ValidateAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        var version = await ReadVersionAsync(connection, transaction, cancellationToken)
            .ConfigureAwait(false);
        if (version > CurrentVersion)
        {
            throw new NotSupportedException(
                $"Relational durable-output schema version {version} is newer than supported version {CurrentVersion}.");
        }

        if (version < CurrentVersion)
        {
            throw new NotSupportedException(
                $"Relational durable-output schema version {version} cannot be migrated to version {CurrentVersion}.");
        }

        await ValidateColumnsAsync(connection, transaction, SchemaTable, ExpectedSchemaColumns, cancellationToken)
            .ConfigureAwait(false);
        await ValidateColumnsAsync(connection, transaction, OutputTable, ExpectedOutputColumns, cancellationToken)
            .ConfigureAwait(false);
        await ValidateColumnsAsync(connection, transaction, DeliveryTable, ExpectedDeliveryColumns, cancellationToken)
            .ConfigureAwait(false);
        await ValidateBinaryKeyCollationAsync(connection, transaction, OutputTable, cancellationToken)
            .ConfigureAwait(false);
        await ValidateBinaryKeyCollationAsync(connection, transaction, DeliveryTable, cancellationToken)
            .ConfigureAwait(false);
        await ValidatePrimaryKeyAsync(connection, transaction, SchemaTable, ["singleton"], cancellationToken)
            .ConfigureAwait(false);
        await ValidatePrimaryKeyAsync(connection, transaction, OutputTable, ["application_address", "message_id"], cancellationToken)
            .ConfigureAwait(false);
        await ValidatePrimaryKeyAsync(connection, transaction, DeliveryTable, ["application_address", "message_id"], cancellationToken)
            .ConfigureAwait(false);
        await ValidateForeignKeyAsync(connection, transaction, cancellationToken)
            .ConfigureAwait(false);
        await ValidateCheckConstraintsAsync(connection, transaction, SchemaTable, 2, cancellationToken)
            .ConfigureAwait(false);
        await ValidateCheckConstraintsAsync(connection, transaction, OutputTable, 7, cancellationToken)
            .ConfigureAwait(false);
        await ValidateCheckConstraintsAsync(connection, transaction, DeliveryTable, 9, cancellationToken)
            .ConfigureAwait(false);
        await ValidateIndexAsync(
                connection,
                transaction,
                EligibilityIndex,
                ["state:0", "next_attempt_utc_ticks:0", "lease_until_utc_ticks:0", "application_address:0", "message_id:0"],
                isFiltered: false,
                cancellationToken)
            .ConfigureAwait(false);
        await ValidateIndexAsync(
                connection,
                transaction,
                DeadLetterIndex,
                ["state:0", "dead_lettered_at_utc_ticks:1", "application_address:0", "message_id:0"],
                isFiltered: true,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async ValueTask<int> ReadVersionAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT CAST(singleton AS int), version FROM dbo.fluxflow_relational_output_schema;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            throw new InvalidDataException("Relational durable-output schema version is missing.");

        var singleton = reader.GetInt32(0);
        var version = reader.GetInt32(1);
        if (singleton != 1 || version <= 0)
            throw new InvalidDataException("Relational durable-output schema version metadata is invalid.");
        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            throw new InvalidDataException("Relational durable-output schema contains multiple version rows.");
        return version;
    }

    private static async ValueTask ValidateColumnsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string table,
        IReadOnlyList<ColumnExpectation> expected,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
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
            actual.Add(new ColumnExpectation(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetInt16(2),
                reader.GetBoolean(3)));

        if (!actual.SequenceEqual(expected))
        {
            throw new InvalidDataException(
                $"Relational durable-output table 'dbo.{table}' has incompatible columns.");
        }
    }

    private static async ValueTask ValidateBinaryKeyCollationAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string table,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT COUNT(*)
            FROM sys.columns AS c
            INNER JOIN sys.tables AS t ON t.object_id = c.object_id
            INNER JOIN sys.schemas AS s ON s.schema_id = t.schema_id
            WHERE s.name = N'dbo' AND t.name = @table
              AND c.name IN (N'application_address', N'message_id')
              AND c.collation_name = N'Latin1_General_100_BIN2';
            """;
        command.Parameters.Add("@table", SqlDbType.NVarChar, 128).Value = table;
        var count = Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
            System.Globalization.CultureInfo.InvariantCulture);
        if (count != 2)
            throw new InvalidDataException($"Relational durable-output table 'dbo.{table}' has incompatible key collation.");
    }

    private static async ValueTask ValidatePrimaryKeyAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string table,
        IReadOnlyList<string> expectedColumns,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
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
            throw new InvalidDataException($"Relational durable-output table 'dbo.{table}' has an incompatible primary key.");
    }

    private static async ValueTask ValidateForeignKeyAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT pc.name, rc.name, rt.name, fk.delete_referential_action
            FROM sys.foreign_keys AS fk
            INNER JOIN sys.foreign_key_columns AS fkc ON fkc.constraint_object_id = fk.object_id
            INNER JOIN sys.tables AS pt ON pt.object_id = fk.parent_object_id
            INNER JOIN sys.columns AS pc
              ON pc.object_id = fkc.parent_object_id AND pc.column_id = fkc.parent_column_id
            INNER JOIN sys.tables AS rt ON rt.object_id = fk.referenced_object_id
            INNER JOIN sys.columns AS rc
              ON rc.object_id = fkc.referenced_object_id AND rc.column_id = fkc.referenced_column_id
            WHERE pt.object_id = OBJECT_ID(N'dbo.fluxflow_relational_output_deliveries')
            ORDER BY fkc.constraint_column_id;
            """;
        var actual = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            actual.Add($"{reader.GetString(0)}:{reader.GetString(1)}:{reader.GetString(2)}:{reader.GetByte(3)}");
        string[] expected =
        [
            $"application_address:application_address:{OutputTable}:1",
            $"message_id:message_id:{OutputTable}:1"
        ];
        if (!actual.SequenceEqual(expected, StringComparer.Ordinal))
            throw new InvalidDataException("Relational durable-output delivery foreign key is missing or incompatible.");
    }

    private static async ValueTask ValidateCheckConstraintsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string table,
        int expectedCount,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
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
            throw new InvalidDataException($"Relational durable-output table 'dbo.{table}' has incompatible check constraints.");
    }

    private static async ValueTask ValidateIndexAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string index,
        IReadOnlyList<string> expected,
        bool isFiltered,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT c.name, ic.is_descending_key, i.has_filter
            FROM sys.indexes AS i
            INNER JOIN sys.index_columns AS ic
              ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            INNER JOIN sys.columns AS c
              ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE i.object_id = OBJECT_ID(N'dbo.fluxflow_relational_output_deliveries')
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
            throw new InvalidDataException($"Relational durable-output index '{index}' is missing or incompatible.");
    }

    private sealed record ColumnExpectation(
        string Name,
        string Type,
        short MaxLength,
        bool IsNullable);
}
