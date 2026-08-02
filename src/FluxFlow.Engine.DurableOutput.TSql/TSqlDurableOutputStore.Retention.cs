using System.Data;
using Microsoft.Data.SqlClient;

namespace FluxFlow.Engine.DurableOutput.TSql;

public sealed partial class TSqlDurableOutputStore
{
    public ValueTask<DurableOutputRetentionResult> PurgeCompletedAsync(
        DurableOutputRetentionRequest request,
        CancellationToken cancellationToken = default)
        => PurgeTerminalAsync(
            request,
            DeliveryCompleted,
            "delivered_at_utc_ticks",
            cancellationToken);

    public ValueTask<DurableOutputRetentionResult> PurgeDeadLettersAsync(
        DurableOutputRetentionRequest request,
        CancellationToken cancellationToken = default)
        => PurgeTerminalAsync(
            request,
            DeliveryDeadLettered,
            "dead_lettered_at_utc_ticks",
            cancellationToken);

    private async ValueTask<DurableOutputRetentionResult> PurgeTerminalAsync(
        DurableOutputRetentionRequest request,
        int terminalState,
        string terminalTimestampColumn,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = await OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken)
            .ConfigureAwait(false);
        await using var command = CreateCommand(connection, transaction);
        command.CommandText = $"""
            ;WITH candidates AS (
                SELECT TOP (@maxCount)
                       delivery.application_address,
                       delivery.message_id
                FROM dbo.fluxflow_relational_output_deliveries AS delivery
                    WITH (UPDLOCK, READPAST, ROWLOCK)
                INNER JOIN dbo.fluxflow_relational_outputs AS capture
                    WITH (UPDLOCK, ROWLOCK)
                  ON capture.application_address = delivery.application_address
                        COLLATE Latin1_General_100_BIN2
                 AND capture.message_id = delivery.message_id
                        COLLATE Latin1_General_100_BIN2
                WHERE delivery.state = @terminalState
                  AND delivery.{terminalTimestampColumn} IS NOT NULL
                  AND delivery.{terminalTimestampColumn} < @terminalBefore
                  AND (@address IS NULL
                       OR delivery.application_address = @address
                            COLLATE Latin1_General_100_BIN2)
                ORDER BY delivery.{terminalTimestampColumn},
                         delivery.application_address COLLATE Latin1_General_100_BIN2,
                         delivery.message_id COLLATE Latin1_General_100_BIN2
            )
            DELETE capture
            FROM dbo.fluxflow_relational_outputs AS capture
            INNER JOIN candidates
              ON candidates.application_address = capture.application_address
                    COLLATE Latin1_General_100_BIN2
             AND candidates.message_id = capture.message_id
                    COLLATE Latin1_General_100_BIN2;
            SELECT @@ROWCOUNT;
            """;
        RelationalDurableOutputRows.Add(
            command,
            "@terminalState",
            SqlDbType.TinyInt,
            terminalState);
        RelationalDurableOutputRows.Add(
            command,
            "@terminalBefore",
            SqlDbType.BigInt,
            request.TerminalBefore.UtcTicks);
        RelationalDurableOutputRows.AddNVarChar(
            command,
            "@address",
            request.Address?.Value,
            300);
        RelationalDurableOutputRows.Add(
            command,
            "@maxCount",
            SqlDbType.Int,
            request.MaxCount);

        var deletedCount = (int)(await command.ExecuteScalarAsync(cancellationToken)
            .ConfigureAwait(false) ?? throw new InvalidDataException(
                "T-SQL durable-output retention did not return a deletion count."));
        cancellationToken.ThrowIfCancellationRequested();
        await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
        return new DurableOutputRetentionResult(deletedCount);
    }
}
