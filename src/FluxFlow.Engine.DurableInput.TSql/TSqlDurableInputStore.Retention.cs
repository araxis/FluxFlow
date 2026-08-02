using System.Data;
using Microsoft.Data.SqlClient;

namespace FluxFlow.Engine.DurableInput.TSql;

public sealed partial class TSqlDurableInputStore
{
    public ValueTask<DurableInputRetentionResult> PurgeDeliveredAsync(
        DurableInputRetentionRequest request,
        CancellationToken cancellationToken = default)
        => PurgeTerminalAsync(
            request,
            DurableInputState.Delivered,
            "delivered_at_utc_ticks",
            cancellationToken);

    public ValueTask<DurableInputRetentionResult> PurgeDeadLettersAsync(
        DurableInputRetentionRequest request,
        CancellationToken cancellationToken = default)
        => PurgeTerminalAsync(
            request,
            DurableInputState.DeadLettered,
            "dead_lettered_at_utc_ticks",
            cancellationToken);

    private async ValueTask<DurableInputRetentionResult> PurgeTerminalAsync(
        DurableInputRetentionRequest request,
        DurableInputState terminalState,
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
                       application_address,
                       message_id
                FROM dbo.fluxflow_relational_inputs WITH (UPDLOCK, READPAST, ROWLOCK)
                WHERE state = @terminalState
                  AND {terminalTimestampColumn} IS NOT NULL
                  AND {terminalTimestampColumn} < @terminalBefore
                  AND (@address IS NULL
                       OR application_address = @address COLLATE Latin1_General_100_BIN2)
                ORDER BY {terminalTimestampColumn},
                         application_address COLLATE Latin1_General_100_BIN2,
                         message_id COLLATE Latin1_General_100_BIN2
            )
            DELETE target
            FROM dbo.fluxflow_relational_inputs AS target
            INNER JOIN candidates
              ON candidates.application_address = target.application_address
                    COLLATE Latin1_General_100_BIN2
             AND candidates.message_id = target.message_id
                    COLLATE Latin1_General_100_BIN2
            WHERE target.state = @terminalState
              AND target.{terminalTimestampColumn} IS NOT NULL
              AND target.{terminalTimestampColumn} < @terminalBefore;
            SELECT @@ROWCOUNT;
            """;
        RelationalDurableInputRows.Add(
            command,
            "@terminalState",
            SqlDbType.TinyInt,
            (byte)terminalState);
        RelationalDurableInputRows.Add(
            command,
            "@terminalBefore",
            SqlDbType.BigInt,
            request.TerminalBefore.UtcTicks);
        RelationalDurableInputRows.AddNVarChar(
            command,
            "@address",
            request.Address?.Value,
            300);
        RelationalDurableInputRows.Add(
            command,
            "@maxCount",
            SqlDbType.Int,
            request.MaxCount);

        var deletedCount = (int)(await command.ExecuteScalarAsync(cancellationToken)
            .ConfigureAwait(false) ?? throw new InvalidDataException(
                "T-SQL durable-input retention did not return a deletion count."));
        cancellationToken.ThrowIfCancellationRequested();
        await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
        return new DurableInputRetentionResult(deletedCount);
    }
}
