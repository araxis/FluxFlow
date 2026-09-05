using System.Data;
using Microsoft.Data.SqlClient;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.DurableInput.TSql.IntegrationTests;

public sealed class TSqlDurableInputResiliencyTests
{
    [Fact]
    public async Task Schema_lock_timeout_is_bounded_leaves_no_partial_schema_and_can_retry()
    {
        await using var database = await TSqlTestDatabase.CreateAsync();
        await using var blocker = await database.OpenConnectionAsync();
        await using var transaction = (SqlTransaction)await blocker.BeginTransactionAsync();
        await using (var lockCommand = blocker.CreateCommand())
        {
            lockCommand.Transaction = transaction;
            lockCommand.CommandText = """
                DECLARE @result int;
                EXEC @result = sys.sp_getapplock
                    @Resource = N'FluxFlow.RelationalDurableInput.Schema',
                    @LockMode = N'Exclusive',
                    @LockOwner = N'Transaction',
                    @LockTimeout = 0,
                    @DbPrincipal = N'public';
                SELECT @result;
                """;
            Convert.ToInt32(await lockCommand.ExecuteScalarAsync()).ShouldBeGreaterThanOrEqualTo(0);
        }

        await using var store = database.CreateStore(new TSqlDurableInputStoreOptions
        {
            ConnectionString = database.ConnectionString,
            SchemaLockTimeout = TimeSpan.FromMilliseconds(100)
        });
        var exception = await Should.ThrowAsync<InvalidOperationException>(() =>
            store.EnqueueAsync(TSqlDurableInputTestSupport.ValueEnvelope("lock-timeout")).AsTask());

        exception.Message.ShouldContain("could not acquire");
        (await database.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.tables WHERE name LIKE N'fluxflow_relational_input%';"))
            .ShouldBe(0);

        await transaction.RollbackAsync();
        (await store.EnqueueAsync(TSqlDurableInputTestSupport.ValueEnvelope("lock-retry")))
            .Status.ShouldBe(DurableInputEnqueueStatus.Enqueued);
    }

    [Fact]
    public async Task External_row_lock_times_out_without_partial_transition_and_recovers_after_rollback()
    {
        await using var database = await TSqlTestDatabase.CreateAsync();
        await using var initializer = database.CreateStore();
        var envelope = TSqlDurableInputTestSupport.ValueEnvelope("row-lock");
        var lease = await TSqlDurableInputTestSupport.EnqueueAndLeaseAsync(initializer, envelope);
        await using var blocker = await database.OpenConnectionAsync();
        await using var transaction = (SqlTransaction)await blocker.BeginTransactionAsync(IsolationLevel.Serializable);
        await using (var command = blocker.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE dbo.fluxflow_relational_inputs
                SET attempt = attempt
                WHERE message_id = N'row-lock';
                """;
            (await command.ExecuteNonQueryAsync()).ShouldBe(1);
        }

        await using var contender = database.CreateStore(new TSqlDurableInputStoreOptions
        {
            ConnectionString = database.ConnectionString,
            CommandTimeout = TimeSpan.FromSeconds(1)
        });
        var transition = new DurableInputLeaseTransition(
            envelope.Key,
            lease.LeaseToken,
            TSqlDurableInputTestSupport.Now.AddMinutes(1));
        var exception = await Should.ThrowAsync<SqlException>(() =>
            contender.MarkDeliveredAsync(transition).AsTask());

        exception.Number.ShouldBe(-2);
        await transaction.RollbackAsync();
        (await contender.MarkDeliveredAsync(transition)).Status
            .ShouldBe(DurableInputTransitionStatus.Applied);
        (await database.ScalarAsync<byte>(
            "SELECT state FROM dbo.fluxflow_relational_inputs WHERE message_id = N'row-lock';"))
            .ShouldBe((byte)DurableInputState.Delivered);
    }

    [Fact]
    public async Task Non_default_timeouts_and_connection_retry_settings_work_against_server()
    {
        await using var database = await TSqlTestDatabase.CreateAsync();
        await using var store = database.CreateStore(new TSqlDurableInputStoreOptions
        {
            ConnectionString = database.ConnectionString,
            CommandTimeout = TimeSpan.FromSeconds(7),
            SchemaLockTimeout = TimeSpan.FromMilliseconds(1250),
            ConnectRetryCount = 3,
            ConnectRetryInterval = TimeSpan.FromSeconds(2)
        });
        var envelope = TSqlDurableInputTestSupport.ValueEnvelope("non-default-options");

        (await store.EnqueueAsync(envelope)).Status.ShouldBe(DurableInputEnqueueStatus.Enqueued);
        var lease = (await store.LeaseAsync(new(
            "options-worker",
            envelope.EnqueuedAt,
            envelope.EnqueuedAt.AddMinutes(1),
            1))).ShouldHaveSingleItem();

        lease.Envelope.ShouldMatchExactly(envelope);
    }
}
