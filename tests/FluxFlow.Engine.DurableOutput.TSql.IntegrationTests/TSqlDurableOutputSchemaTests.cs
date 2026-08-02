using Microsoft.Data.SqlClient;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.DurableOutput.TSql.IntegrationTests;

public sealed class TSqlDurableOutputSchemaTests
{
    [Fact]
    public async Task First_operation_creates_exact_version_one_schema_collations_constraints_foreign_key_and_indexes()
    {
        await using var database = await TSqlTestDatabase.CreateAsync();
        await using var store = database.CreateStore();
        await store.EnqueueAsync(TSqlDurableOutputTestSupport.ValueEnvelope("schema-v1"));

        (await TSqlDurableOutputTestSupport.ReadStringsAsync(
            database,
            """
            SELECT s.name, t.name
            FROM sys.tables AS t
            INNER JOIN sys.schemas AS s ON s.schema_id = t.schema_id
            WHERE t.name LIKE N'fluxflow_relational_output%'
            ORDER BY s.name, t.name;
            """)).ShouldBe([
                "dbo|fluxflow_relational_output_deliveries",
                "dbo|fluxflow_relational_output_schema",
                "dbo|fluxflow_relational_outputs"
            ]);
        (await TSqlDurableOutputTestSupport.ReadStringsAsync(
            database,
            "SELECT CAST(singleton AS int), version FROM dbo.fluxflow_relational_output_schema;"))
            .ShouldBe(["1|1"]);
        (await TSqlDurableOutputTestSupport.ScalarAsync<int>(
            database,
            "SELECT is_read_committed_snapshot_on FROM sys.databases WHERE name = DB_NAME();"))
            .ShouldBe(0);

        (await ReadColumnShapeAsync(database, RelationalDurableOutputSchema.SchemaTable))
            .ShouldBe([
                "singleton|bit|1|0|<null>",
                "version|int|4|0|<null>"
            ]);
        (await ReadColumnShapeAsync(database, RelationalDurableOutputSchema.OutputTable))
            .ShouldBe([
                "application_address|nvarchar|600|0|Latin1_General_100_BIN2",
                "message_id|nvarchar|256|0|Latin1_General_100_BIN2",
                "contract_name|nvarchar|2048|0|" + await DatabaseCollationAsync(database),
                "envelope_schema_version|int|4|0|<null>",
                "is_error|bit|1|0|<null>",
                "payload_json|nvarchar|-1|0|" + await DatabaseCollationAsync(database),
                "error_code|nvarchar|2048|1|" + await DatabaseCollationAsync(database),
                "error_message|nvarchar|-1|1|" + await DatabaseCollationAsync(database),
                "error_category|nvarchar|2048|1|" + await DatabaseCollationAsync(database),
                "error_is_transient|bit|1|1|<null>",
                "error_details_json|nvarchar|-1|1|" + await DatabaseCollationAsync(database),
                "trace_id|nvarchar|1024|0|" + await DatabaseCollationAsync(database),
                "correlation_id|nvarchar|1024|1|" + await DatabaseCollationAsync(database),
                "causation_id|nvarchar|1024|1|" + await DatabaseCollationAsync(database),
                "message_timestamp_utc_ticks|bigint|8|0|<null>",
                "message_timestamp_offset_minutes|smallint|2|0|<null>",
                "captured_at_utc_ticks|bigint|8|0|<null>",
                "captured_at_offset_minutes|smallint|2|0|<null>",
                "headers_json|nvarchar|-1|0|" + await DatabaseCollationAsync(database)
            ]);
        (await ReadColumnShapeAsync(database, RelationalDurableOutputSchema.DeliveryTable))
            .ShouldBe([
                "application_address|nvarchar|600|0|Latin1_General_100_BIN2",
                "message_id|nvarchar|256|0|Latin1_General_100_BIN2",
                "state|tinyint|1|0|<null>",
                "next_attempt_utc_ticks|bigint|8|0|<null>",
                "next_attempt_offset_minutes|smallint|2|0|<null>",
                "lease_token|uniqueidentifier|16|1|<null>",
                "lease_owner|nvarchar|1024|1|" + await DatabaseCollationAsync(database),
                "leased_at_utc_ticks|bigint|8|1|<null>",
                "leased_at_offset_minutes|smallint|2|1|<null>",
                "lease_until_utc_ticks|bigint|8|1|<null>",
                "lease_until_offset_minutes|smallint|2|1|<null>",
                "attempt|int|4|0|<null>",
                "delivered_at_utc_ticks|bigint|8|1|<null>",
                "delivered_at_offset_minutes|smallint|2|1|<null>",
                "dead_letter_reason|int|4|1|<null>",
                "dead_lettered_at_utc_ticks|bigint|8|1|<null>",
                "dead_lettered_at_offset_minutes|smallint|2|1|<null>",
                "dead_letter_generation|bigint|8|0|<null>"
            ]);

        (await TSqlDurableOutputTestSupport.ReadStringsAsync(
            database,
            """
            SELECT t.name, i.name, ic.key_ordinal, c.name
            FROM sys.indexes AS i
            INNER JOIN sys.tables AS t ON t.object_id = i.object_id
            INNER JOIN sys.index_columns AS ic
              ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            INNER JOIN sys.columns AS c
              ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE i.is_primary_key = 1 AND t.name LIKE N'fluxflow_relational_output%'
            ORDER BY t.name, ic.key_ordinal;
            """)).ShouldBe([
                "fluxflow_relational_output_deliveries|pk_fluxflow_relational_output_deliveries|1|application_address",
                "fluxflow_relational_output_deliveries|pk_fluxflow_relational_output_deliveries|2|message_id",
                "fluxflow_relational_output_schema|pk_fluxflow_relational_output_schema|1|singleton",
                "fluxflow_relational_outputs|pk_fluxflow_relational_outputs|1|application_address",
                "fluxflow_relational_outputs|pk_fluxflow_relational_outputs|2|message_id"
            ]);
        (await TSqlDurableOutputTestSupport.ReadStringsAsync(
            database,
            """
            SELECT pc.name, rc.name, rt.name, fk.delete_referential_action_desc
            FROM sys.foreign_keys AS fk
            INNER JOIN sys.foreign_key_columns AS fkc ON fkc.constraint_object_id = fk.object_id
            INNER JOIN sys.columns AS pc
              ON pc.object_id = fkc.parent_object_id AND pc.column_id = fkc.parent_column_id
            INNER JOIN sys.tables AS rt ON rt.object_id = fk.referenced_object_id
            INNER JOIN sys.columns AS rc
              ON rc.object_id = fkc.referenced_object_id AND rc.column_id = fkc.referenced_column_id
            WHERE fk.parent_object_id = OBJECT_ID(N'dbo.fluxflow_relational_output_deliveries')
            ORDER BY fkc.constraint_column_id;
            """)).ShouldBe([
                "application_address|application_address|fluxflow_relational_outputs|CASCADE",
                "message_id|message_id|fluxflow_relational_outputs|CASCADE"
            ]);

        var constraints = await TSqlDurableOutputTestSupport.ReadStringsAsync(
            database,
            """
            SELECT t.name, cc.name, CAST(cc.is_disabled AS int), CAST(cc.is_not_trusted AS int)
            FROM sys.check_constraints AS cc
            INNER JOIN sys.tables AS t ON t.object_id = cc.parent_object_id
            WHERE t.name LIKE N'fluxflow_relational_output%'
            ORDER BY t.name, cc.name;
            """);
        constraints.Count.ShouldBe(18);
        constraints.ShouldAllBe(static value => value.EndsWith("|0|0", StringComparison.Ordinal));
        constraints.ShouldContain("fluxflow_relational_output_schema|ck_fluxflow_relational_output_schema_singleton|0|0");
        constraints.ShouldContain("fluxflow_relational_outputs|ck_fluxflow_relational_outputs_shape|0|0");
        constraints.ShouldContain("fluxflow_relational_output_deliveries|ck_fluxflow_relational_output_deliveries_shape|0|0");

        (await TSqlDurableOutputTestSupport.ReadStringsAsync(
            database,
            """
            SELECT i.name, c.name, ic.key_ordinal, CAST(ic.is_descending_key AS int),
                   CAST(i.has_filter AS int)
            FROM sys.indexes AS i
            INNER JOIN sys.index_columns AS ic
              ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            INNER JOIN sys.columns AS c
              ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE i.object_id = OBJECT_ID(N'dbo.fluxflow_relational_output_deliveries')
              AND i.name IN (
                  N'ix_fluxflow_relational_output_deliveries_eligibility',
                  N'ix_fluxflow_relational_output_deliveries_dead_lettered')
            ORDER BY i.name, ic.key_ordinal;
            """)).ShouldBe([
                "ix_fluxflow_relational_output_deliveries_dead_lettered|state|1|0|1",
                "ix_fluxflow_relational_output_deliveries_dead_lettered|dead_lettered_at_utc_ticks|2|1|1",
                "ix_fluxflow_relational_output_deliveries_dead_lettered|application_address|3|0|1",
                "ix_fluxflow_relational_output_deliveries_dead_lettered|message_id|4|0|1",
                "ix_fluxflow_relational_output_deliveries_eligibility|state|1|0|0",
                "ix_fluxflow_relational_output_deliveries_eligibility|next_attempt_utc_ticks|2|0|0",
                "ix_fluxflow_relational_output_deliveries_eligibility|lease_until_utc_ticks|3|0|0",
                "ix_fluxflow_relational_output_deliveries_eligibility|application_address|4|0|0",
                "ix_fluxflow_relational_output_deliveries_eligibility|message_id|5|0|0"
            ]);
        (await TSqlDurableOutputTestSupport.ScalarAsync<string>(
            database,
            "SELECT filter_definition FROM sys.indexes WHERE name = N'ix_fluxflow_relational_output_deliveries_dead_lettered';"))
            .ShouldBe("([state]=(4))");
    }

    [Fact]
    public async Task Concurrent_first_use_by_multiple_stores_initializes_one_exact_schema()
    {
        await using var database = await TSqlTestDatabase.CreateAsync();
        var stores = Enumerable.Range(0, 8).Select(_ => database.CreateStore()).ToArray();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var operations = stores.Select((store, index) => Task.Run(async () =>
        {
            await start.Task;
            return await store.EnqueueAsync(
                TSqlDurableOutputTestSupport.ValueEnvelope($"initialize-{index:D2}"));
        })).ToArray();

        start.TrySetResult();
        var results = await Task.WhenAll(operations);

        results.Select(static result => result.Status)
            .ShouldAllBe(static status => status == DurableOutputEnqueueStatus.Enqueued);
        results.Select(static result => result.Key).Distinct().Count().ShouldBe(stores.Length);
        (await TSqlDurableOutputTestSupport.ScalarAsync<int>(
            database,
            "SELECT COUNT(*) FROM dbo.fluxflow_relational_output_schema WHERE singleton = 1 AND version = 1;"))
            .ShouldBe(1);
        (await TSqlDurableOutputTestSupport.ScalarAsync<int>(
            database,
            "SELECT COUNT(*) FROM sys.tables WHERE name LIKE N'fluxflow_relational_output%';"))
            .ShouldBe(3);
        (await TSqlDurableOutputTestSupport.ScalarAsync<int>(
            database,
            "SELECT COUNT(*) FROM dbo.fluxflow_relational_outputs;"))
            .ShouldBe(stores.Length);
    }

    [Fact]
    public async Task Future_schema_version_is_rejected_without_downgrade()
    {
        await using var database = await CreateInitializedDatabaseAsync("future-schema");
        await TSqlDurableOutputTestSupport.ExecuteAsync(
            database,
            "UPDATE dbo.fluxflow_relational_output_schema SET version = 2;");
        await using var store = database.CreateStore();

        var exception = await Should.ThrowAsync<NotSupportedException>(() =>
            store.ReadAsync(TSqlDurableOutputTestSupport.ValueEnvelope("future-read").Key)
                .AsTask());

        exception.Message.ShouldBe(
            "Relational durable-output schema version 2 is newer than supported version 1.");
        (await TSqlDurableOutputTestSupport.ScalarAsync<int>(
            database,
            "SELECT version FROM dbo.fluxflow_relational_output_schema;"))
            .ShouldBe(2);
        (await TSqlDurableOutputTestSupport.ScalarAsync<int>(
            database,
            "SELECT COUNT(*) FROM sys.tables WHERE name LIKE N'fluxflow_relational_output%';"))
            .ShouldBe(3);
    }

    [Fact]
    public async Task Partial_schema_is_rejected_without_repair()
    {
        await using var database = await TSqlTestDatabase.CreateAsync();
        await TSqlDurableOutputTestSupport.ExecuteAsync(
            database,
            "CREATE TABLE dbo.fluxflow_relational_output_schema (singleton bit NOT NULL, version int NOT NULL);");
        await using var store = database.CreateStore();

        var exception = await Should.ThrowAsync<InvalidDataException>(() =>
            store.EnqueueAsync(TSqlDurableOutputTestSupport.ValueEnvelope("partial"))
                .AsTask());

        exception.Message.ShouldBe(
            "Relational durable-output schema is partial and cannot be repaired automatically.");
        (await TSqlDurableOutputTestSupport.ReadStringsAsync(
            database,
            "SELECT name FROM sys.tables WHERE name LIKE N'fluxflow_relational_output%' ORDER BY name;"))
            .ShouldBe(["fluxflow_relational_output_schema"]);
    }

    [Fact]
    public async Task Missing_required_index_is_rejected_without_repair()
    {
        await using var database = await CreateInitializedDatabaseAsync("missing-index");
        await TSqlDurableOutputTestSupport.ExecuteAsync(
            database,
            "DROP INDEX ix_fluxflow_relational_output_deliveries_eligibility ON dbo.fluxflow_relational_output_deliveries;");
        await using var store = database.CreateStore();

        var exception = await Should.ThrowAsync<InvalidDataException>(() =>
            store.ReadAsync(TSqlDurableOutputTestSupport.ValueEnvelope("missing-index-read").Key)
                .AsTask());

        exception.Message.ShouldContain(RelationalDurableOutputSchema.EligibilityIndex);
        (await TSqlDurableOutputTestSupport.ScalarAsync<int>(
            database,
            "SELECT COUNT(*) FROM sys.indexes WHERE name = N'ix_fluxflow_relational_output_deliveries_eligibility';"))
            .ShouldBe(0);
        (await TSqlDurableOutputTestSupport.ScalarAsync<int>(
            database,
            "SELECT COUNT(*) FROM sys.indexes WHERE name = N'ix_fluxflow_relational_output_deliveries_dead_lettered';"))
            .ShouldBe(1);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("zero")]
    [InlineData("multiple")]
    public async Task Corrupt_version_metadata_is_rejected_without_repair(string corruption)
    {
        await using var database = await CreateInitializedDatabaseAsync($"version-{corruption}");
        await DamageVersionAsync(database, corruption);
        await using var store = database.CreateStore();

        var exception = await Should.ThrowAsync<InvalidDataException>(() =>
            store.ReadAsync(TSqlDurableOutputTestSupport.ValueEnvelope("metadata-read").Key)
                .AsTask());

        exception.Message.ShouldContain("schema");
        var expectedRows = corruption == "multiple" ? 2 : corruption == "missing" ? 0 : 1;
        (await TSqlDurableOutputTestSupport.ScalarAsync<int>(
            database,
            "SELECT COUNT(*) FROM dbo.fluxflow_relational_output_schema;"))
            .ShouldBe(expectedRows);
        if (corruption == "zero")
        {
            (await TSqlDurableOutputTestSupport.ScalarAsync<int>(
                database,
                "SELECT version FROM dbo.fluxflow_relational_output_schema;"))
                .ShouldBe(0);
        }
    }

    [Fact]
    public async Task Incompatible_required_column_is_rejected_without_repair()
    {
        await using var database = await CreateInitializedDatabaseAsync("column-shape");
        await TSqlDurableOutputTestSupport.ExecuteAsync(
            database,
            "ALTER TABLE dbo.fluxflow_relational_outputs ALTER COLUMN contract_name nvarchar(1000) NOT NULL;");
        await using var store = database.CreateStore();

        var exception = await Should.ThrowAsync<InvalidDataException>(() =>
            store.ReadAsync(TSqlDurableOutputTestSupport.ValueEnvelope("column-read").Key)
                .AsTask());

        exception.Message.ShouldContain("incompatible columns");
        (await TSqlDurableOutputTestSupport.ScalarAsync<int>(
            database,
            """
            SELECT max_length
            FROM sys.columns
            WHERE object_id = OBJECT_ID(N'dbo.fluxflow_relational_outputs')
              AND name = N'contract_name';
            """)).ShouldBe(2000);
    }

    private static ValueTask<IReadOnlyList<string>> ReadColumnShapeAsync(
        TSqlTestDatabase database,
        string table)
        => TSqlDurableOutputTestSupport.ReadStringsAsync(
            database,
            """
            SELECT c.name, ty.name, c.max_length, CAST(c.is_nullable AS int),
                   COALESCE(c.collation_name, N'<null>')
            FROM sys.columns AS c
            INNER JOIN sys.types AS ty ON ty.user_type_id = c.user_type_id
            WHERE c.object_id = OBJECT_ID(N'dbo.' + @table)
            ORDER BY c.column_id;
            """,
            command => TSqlDurableOutputTestSupport.AddKeyParameter(
                command,
                "@table",
                table,
                128));

    private static ValueTask<string> DatabaseCollationAsync(TSqlTestDatabase database)
        => TSqlDurableOutputTestSupport.ScalarAsync<string>(
            database,
            "SELECT CONVERT(nvarchar(128), DATABASEPROPERTYEX(DB_NAME(), 'Collation')); ");

    private static async ValueTask<TSqlTestDatabase> CreateInitializedDatabaseAsync(
        string messageId)
    {
        var database = await TSqlTestDatabase.CreateAsync();
        try
        {
            await using var store = database.CreateStore();
            await store.EnqueueAsync(TSqlDurableOutputTestSupport.ValueEnvelope(messageId));
            return database;
        }
        catch
        {
            await database.DisposeAsync();
            throw;
        }
    }

    private static ValueTask DamageVersionAsync(
        TSqlTestDatabase database,
        string corruption)
        => corruption switch
        {
            "missing" => TSqlDurableOutputTestSupport.ExecuteAsync(
                database,
                "DELETE FROM dbo.fluxflow_relational_output_schema;"),
            "zero" => TSqlDurableOutputTestSupport.ExecuteAsync(
                database,
                """
                ALTER TABLE dbo.fluxflow_relational_output_schema
                    NOCHECK CONSTRAINT ck_fluxflow_relational_output_schema_version;
                UPDATE dbo.fluxflow_relational_output_schema SET version = 0;
                """),
            "multiple" => TSqlDurableOutputTestSupport.ExecuteAsync(
                database,
                """
                ALTER TABLE dbo.fluxflow_relational_output_schema
                    DROP CONSTRAINT pk_fluxflow_relational_output_schema;
                ALTER TABLE dbo.fluxflow_relational_output_schema
                    NOCHECK CONSTRAINT ck_fluxflow_relational_output_schema_singleton;
                INSERT INTO dbo.fluxflow_relational_output_schema (singleton, version)
                VALUES (0, 1);
                """),
            _ => throw new ArgumentOutOfRangeException(nameof(corruption))
        };
}
