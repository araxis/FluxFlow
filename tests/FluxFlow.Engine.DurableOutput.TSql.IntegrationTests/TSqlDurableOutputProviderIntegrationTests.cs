using System.Data;
using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.DurableOutput.TSql.IntegrationTests;

public sealed class TSqlDurableOutputProviderIntegrationTests
{
    [Fact]
    public async Task Validate_only_rejects_an_absent_schema_without_creating_objects()
    {
        await using var database = await TSqlTestDatabase.CreateAsync();
        await using var store = database.CreateStore(new TSqlDurableOutputStoreOptions
        {
            ConnectionString = database.ConnectionString,
            SchemaManagement = TSqlDurableOutputSchemaManagement.ValidateOnly
        });

        var exception = await Should.ThrowAsync<InvalidOperationException>(() =>
            store.EnqueueAsync(TSqlDurableOutputTestSupport.ValueEnvelope("validate-only-absent"))
                .AsTask());

        exception.Message.ShouldBe(
            "T-SQL durable-output schema is absent and validate-only mode cannot create it.");
        (await OwnedTableCountAsync(database)).ShouldBe(0);
    }

    [Fact]
    public async Task Validate_only_accepts_an_existing_exact_schema_and_preserves_data()
    {
        await using var database = await TSqlTestDatabase.CreateAsync();
        var envelope = TSqlDurableOutputTestSupport.ValueEnvelope("validate-only-existing");
        await using (var creator = database.CreateStore())
            (await creator.EnqueueAsync(envelope)).Status.ShouldBe(DurableOutputEnqueueStatus.Enqueued);

        await using var validator = database.CreateStore(new TSqlDurableOutputStoreOptions
        {
            ConnectionString = database.ConnectionString,
            SchemaManagement = TSqlDurableOutputSchemaManagement.ValidateOnly
        });

        (await validator.ReadAsync(envelope.Key)).ShouldNotBeNull().ShouldMatchExactly(envelope);
        (await OwnedTableCountAsync(database)).ShouldBe(3);
    }

    [Fact]
    public async Task Read_committed_snapshot_is_rejected_before_schema_creation()
    {
        await using var database = await TSqlTestDatabase.CreateAsync();
        await database.SetReadCommittedSnapshotAsync(enabled: true);
        await using var store = database.CreateStore();

        var exception = await Should.ThrowAsync<InvalidOperationException>(() =>
            store.EnqueueAsync(TSqlDurableOutputTestSupport.ValueEnvelope("rcsi-enabled"))
                .AsTask());

        exception.Message.ShouldBe(
            "T-SQL durable output requires READ_COMMITTED_SNAPSHOT to be disabled for its locking lease strategy.");
        (await OwnedTableCountAsync(database)).ShouldBe(0);
    }

    [Fact]
    public async Task Non_default_timeouts_and_connection_retry_settings_work_against_the_server()
    {
        await using var database = await TSqlTestDatabase.CreateAsync();
        var options = new TSqlDurableOutputStoreOptions
        {
            ConnectionString = database.ConnectionString,
            CommandTimeout = TimeSpan.FromSeconds(7),
            SchemaLockTimeout = TimeSpan.FromMilliseconds(1250),
            ConnectRetryCount = 2,
            ConnectRetryInterval = TimeSpan.FromSeconds(3)
        };
        var settings = options.Resolve();
        var connection = new SqlConnectionStringBuilder(settings.NormalizedConnectionString);
        await using var store = database.CreateStore(options);
        var envelope = TSqlDurableOutputTestSupport.ValueEnvelope("non-default-settings");

        settings.CommandTimeoutSeconds.ShouldBe(7);
        settings.SchemaLockTimeoutMilliseconds.ShouldBe(1250);
        connection.ConnectRetryCount.ShouldBe(2);
        connection.ConnectRetryInterval.ShouldBe(3);
        (await store.EnqueueAsync(envelope)).Status.ShouldBe(DurableOutputEnqueueStatus.Enqueued);
        (await store.ReadAsync(envelope.Key)).ShouldNotBeNull().ShouldMatchExactly(envelope);
    }

    [Fact]
    public async Task Pre_cancelled_operation_does_not_initialize_the_schema()
    {
        await using var database = await TSqlTestDatabase.CreateAsync();
        await using var store = database.CreateStore();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(() =>
            store.EnqueueAsync(
                    TSqlDurableOutputTestSupport.ValueEnvelope("cancelled-before-command"),
                    cancellation.Token)
                .AsTask());

        (await OwnedTableCountAsync(database)).ShouldBe(0);
    }

    [Fact]
    public async Task Overlength_contract_is_rejected_before_schema_initialization()
    {
        await using var database = await TSqlTestDatabase.CreateAsync();
        await using var store = database.CreateStore();
        var source = TSqlDurableOutputTestSupport.ValueEnvelope("overlength-contract");
        var envelope = new DurableOutputEnvelope(
            source.Address,
            new string('c', 1025),
            source.IsError,
            source.Payload,
            source.Error,
            source.MessageId,
            source.TraceId,
            source.Timestamp,
            source.CapturedAt,
            source.CorrelationId,
            source.CausationId,
            source.Headers,
            source.SchemaVersion);

        var exception = await Should.ThrowAsync<ArgumentException>(() =>
            store.EnqueueAsync(envelope).AsTask());

        exception.ParamName.ShouldBe("envelope");
        exception.Message.ShouldContain("contract name");
        exception.Message.ShouldContain("1024");
        (await OwnedTableCountAsync(database)).ShouldBe(0);
    }

    [Fact]
    public async Task Schema_lock_timeout_is_bounded_leaves_no_partial_schema_and_can_retry()
    {
        await using var database = await TSqlTestDatabase.CreateAsync();
        await using var lockConnection = await database.OpenConnectionAsync();
        await using var transaction = (SqlTransaction)await lockConnection.BeginTransactionAsync();
        await using (var command = lockConnection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                DECLARE @result int;
                EXEC @result = sys.sp_getapplock
                    @Resource = N'FluxFlow.RelationalDurableOutput.Schema',
                    @LockMode = 'Exclusive',
                    @LockOwner = 'Transaction',
                    @LockTimeout = 0;
                SELECT @result;
                """;
            Convert.ToInt32(await command.ExecuteScalarAsync()).ShouldBeGreaterThanOrEqualTo(0);
        }

        await using var store = database.CreateStore(new TSqlDurableOutputStoreOptions
        {
            ConnectionString = database.ConnectionString,
            CommandTimeout = TimeSpan.FromSeconds(1),
            SchemaLockTimeout = TimeSpan.FromMilliseconds(250)
        });
        var envelope = TSqlDurableOutputTestSupport.ValueEnvelope("schema-lock-timeout");
        var stopwatch = Stopwatch.StartNew();

        var exception = await Should.ThrowAsync<InvalidOperationException>(() =>
            store.EnqueueAsync(envelope).AsTask());
        stopwatch.Stop();

        exception.Message.ShouldBe(
            "Relational durable-output schema initialization could not acquire its database lock.");
        stopwatch.Elapsed.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(100));
        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5));
        (await OwnedTableCountAsync(database)).ShouldBe(0);

        await transaction.RollbackAsync();
        (await store.EnqueueAsync(envelope)).Status.ShouldBe(DurableOutputEnqueueStatus.Enqueued);
    }

    [Fact]
    public async Task Schema_failure_does_not_reveal_connection_configuration_or_mutate_partial_schema()
    {
        const string secret = "schema-configuration-secret-sentinel";
        await using var database = await TSqlTestDatabase.CreateAsync();
        await TSqlDurableOutputTestSupport.ExecuteAsync(
            database,
            "CREATE TABLE dbo.fluxflow_relational_output_schema (singleton bit NOT NULL, version int NOT NULL);");
        var connection = new SqlConnectionStringBuilder(database.ConnectionString)
        {
            ApplicationName = secret
        };
        await using var store = database.CreateStore(new TSqlDurableOutputStoreOptions
        {
            ConnectionString = connection.ConnectionString
        });

        var exception = await Should.ThrowAsync<InvalidDataException>(() =>
            store.EnqueueAsync(TSqlDurableOutputTestSupport.ValueEnvelope("schema-redaction"))
                .AsTask());

        exception.Message.ShouldBe(
            "Relational durable-output schema is partial and cannot be repaired automatically.");
        exception.ToString().ShouldNotContain(secret, Case.Insensitive);
        (await OwnedTableCountAsync(database)).ShouldBe(1);
    }

    private static ValueTask<int> OwnedTableCountAsync(TSqlTestDatabase database)
        => TSqlDurableOutputTestSupport.ScalarAsync<int>(
            database,
            "SELECT COUNT(*) FROM sys.tables WHERE name LIKE N'fluxflow_relational_output%';");
}
