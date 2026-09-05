using Shouldly;
using Xunit;

namespace FluxFlow.Engine.DurableInput.TSql.IntegrationTests;

public sealed class TSqlDurableInputSchemaTests
{
    [Fact]
    public async Task First_operation_creates_exact_version_one_schema_binary_keys_constraints_and_indexes()
    {
        await using var database = await TSqlTestDatabase.CreateAsync();
        await using var store = database.CreateStore();

        (await store.EnqueueAsync(TSqlDurableInputTestSupport.ValueEnvelope("schema-first")))
            .Status.ShouldBe(DurableInputEnqueueStatus.Enqueued);

        (await database.ScalarAsync<int>(
            "SELECT version FROM dbo.fluxflow_relational_input_schema WHERE singleton = 1;"))
            .ShouldBe(1);
        (await database.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.fluxflow_relational_inputs');"))
            .ShouldBe(31);
        (await database.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.fluxflow_relational_inputs') AND name IN (N'ix_fluxflow_relational_inputs_eligibility', N'ix_fluxflow_relational_inputs_dead_lettered');"))
            .ShouldBe(2);
        (await database.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.fluxflow_relational_inputs');"))
            .ShouldBe(10);
        (await database.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.fluxflow_relational_input_schema');"))
            .ShouldBe(2);
        (await database.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id IN (OBJECT_ID(N'dbo.fluxflow_relational_inputs'), OBJECT_ID(N'dbo.fluxflow_relational_input_schema')) AND (is_disabled = 1 OR is_not_trusted = 1);"))
            .ShouldBe(0);
        (await database.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.fluxflow_relational_inputs') AND name IN (N'application_address', N'message_id') AND collation_name = N'Latin1_General_100_BIN2';"))
            .ShouldBe(2);
        (await database.ScalarAsync<int>(
            "SELECT is_read_committed_snapshot_on FROM sys.databases WHERE name = DB_NAME();"))
            .ShouldBe(0);
    }

    [Fact]
    public async Task Concurrent_first_use_by_multiple_stores_initializes_one_exact_schema()
    {
        await using var database = await TSqlTestDatabase.CreateAsync();
        await using var first = database.CreateStore();
        await using var second = database.CreateStore();

        var results = await Task.WhenAll(
            first.EnqueueAsync(TSqlDurableInputTestSupport.ValueEnvelope("concurrent-a")).AsTask(),
            second.EnqueueAsync(TSqlDurableInputTestSupport.ValueEnvelope("concurrent-b")).AsTask());

        results.ShouldAllBe(result => result.Status == DurableInputEnqueueStatus.Enqueued);
        (await database.ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.fluxflow_relational_input_schema WHERE singleton = 1 AND version = 1;"))
            .ShouldBe(1);
        (await database.ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.fluxflow_relational_inputs;"))
            .ShouldBe(2);
    }

    [Fact]
    public async Task Validate_only_rejects_absent_schema_without_creating_objects()
    {
        await using var database = await TSqlTestDatabase.CreateAsync();
        await using var store = database.CreateStore(new TSqlDurableInputStoreOptions
        {
            ConnectionString = database.ConnectionString,
            SchemaManagement = TSqlDurableInputSchemaManagement.ValidateOnly
        });

        var exception = await Should.ThrowAsync<InvalidOperationException>(() =>
            store.EnqueueAsync(TSqlDurableInputTestSupport.ValueEnvelope("validate-absent")).AsTask());

        exception.Message.ShouldContain("schema is absent");
        (await database.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.tables WHERE name LIKE N'fluxflow_relational_input%';"))
            .ShouldBe(0);
    }

    [Fact]
    public async Task Validate_only_accepts_existing_exact_schema_and_preserves_data()
    {
        await using var database = await TSqlTestDatabase.CreateAsync();
        var firstEnvelope = TSqlDurableInputTestSupport.ValueEnvelope("validate-existing-a");
        await using (var initializer = database.CreateStore())
            (await initializer.EnqueueAsync(firstEnvelope)).Status.ShouldBe(DurableInputEnqueueStatus.Enqueued);

        await using var validator = database.CreateStore(new TSqlDurableInputStoreOptions
        {
            ConnectionString = database.ConnectionString,
            SchemaManagement = TSqlDurableInputSchemaManagement.ValidateOnly
        });
        (await validator.EnqueueAsync(TSqlDurableInputTestSupport.ValueEnvelope("validate-existing-b")))
            .Status.ShouldBe(DurableInputEnqueueStatus.Enqueued);

        (await database.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.fluxflow_relational_inputs;"))
            .ShouldBe(2);
    }

    [Fact]
    public async Task Read_committed_snapshot_is_rejected_before_schema_creation()
    {
        await using var database = await TSqlTestDatabase.CreateAsync();
        await database.SetReadCommittedSnapshotAsync(enabled: true);
        await using var store = database.CreateStore();

        var exception = await Should.ThrowAsync<InvalidOperationException>(() =>
            store.EnqueueAsync(TSqlDurableInputTestSupport.ValueEnvelope("rcsi")).AsTask());

        exception.Message.ShouldContain("READ_COMMITTED_SNAPSHOT");
        (await database.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.tables WHERE name LIKE N'fluxflow_relational_input%';"))
            .ShouldBe(0);
    }

    [Fact]
    public async Task Partial_schema_is_rejected_without_repair()
    {
        await using var database = await TSqlTestDatabase.CreateAsync();
        await database.ExecuteAsync("""
            CREATE TABLE dbo.fluxflow_relational_input_schema (
                singleton bit NOT NULL PRIMARY KEY,
                version int NOT NULL);
            """);
        await using var store = database.CreateStore();

        var exception = await Should.ThrowAsync<InvalidDataException>(() =>
            store.EnqueueAsync(TSqlDurableInputTestSupport.ValueEnvelope("partial")).AsTask());

        exception.Message.ShouldBe(
            "Relational durable-input schema is partial and cannot be repaired automatically.");
        (await database.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.tables WHERE name = N'fluxflow_relational_inputs';"))
            .ShouldBe(0);
    }

    [Fact]
    public async Task Future_schema_version_is_rejected_without_downgrade()
    {
        await using var database = await TSqlTestDatabase.CreateAsync();
        await using (var initializer = database.CreateStore())
            await initializer.EnqueueAsync(TSqlDurableInputTestSupport.ValueEnvelope("future-init"));
        await database.ExecuteAsync(
            "UPDATE dbo.fluxflow_relational_input_schema SET version = 2 WHERE singleton = 1;");
        await using var validator = database.CreateStore();

        var exception = await Should.ThrowAsync<NotSupportedException>(() =>
            validator.EnqueueAsync(TSqlDurableInputTestSupport.ValueEnvelope("future-attempt")).AsTask());

        exception.Message.ShouldContain("newer than supported version 1");
        (await database.ScalarAsync<int>(
            "SELECT version FROM dbo.fluxflow_relational_input_schema WHERE singleton = 1;"))
            .ShouldBe(2);
    }

    [Fact]
    public async Task Missing_version_metadata_is_rejected_without_repair()
    {
        await using var database = await TSqlTestDatabase.CreateAsync();
        await using (var initializer = database.CreateStore())
            await initializer.EnqueueAsync(TSqlDurableInputTestSupport.ValueEnvelope("metadata-init"));
        await database.ExecuteAsync("DELETE FROM dbo.fluxflow_relational_input_schema;");
        await using var validator = database.CreateStore();

        var exception = await Should.ThrowAsync<InvalidDataException>(() =>
            validator.EnqueueAsync(TSqlDurableInputTestSupport.ValueEnvelope("metadata-attempt")).AsTask());

        exception.Message.ShouldContain("version is missing");
        (await database.ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.fluxflow_relational_input_schema;"))
            .ShouldBe(0);
    }

    [Fact]
    public async Task Missing_required_index_is_rejected_without_repair()
    {
        await using var database = await TSqlTestDatabase.CreateAsync();
        await using (var initializer = database.CreateStore())
            await initializer.EnqueueAsync(TSqlDurableInputTestSupport.ValueEnvelope("index-init"));
        await database.ExecuteAsync(
            "DROP INDEX ix_fluxflow_relational_inputs_eligibility ON dbo.fluxflow_relational_inputs;");
        await using var validator = database.CreateStore();

        var exception = await Should.ThrowAsync<InvalidDataException>(() =>
            validator.EnqueueAsync(TSqlDurableInputTestSupport.ValueEnvelope("index-attempt")).AsTask());

        exception.Message.ShouldContain("eligibility");
        (await database.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.fluxflow_relational_inputs') AND name = N'ix_fluxflow_relational_inputs_eligibility';"))
            .ShouldBe(0);
    }
}
