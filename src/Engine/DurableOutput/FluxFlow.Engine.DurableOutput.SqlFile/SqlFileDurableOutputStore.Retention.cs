using Microsoft.Data.Sqlite;

namespace FluxFlow.Engine.DurableOutput.SqlFile;

public sealed partial class SqlFileDurableOutputStore
{
    public ValueTask<DurableOutputRetentionResult> PurgeCompletedAsync(
        DurableOutputRetentionRequest request,
        CancellationToken cancellationToken = default)
        => PurgeTerminalAsync(
            request,
            DeliveryCompleted,
            "delivered_at_utc_ticks",
            "completed retention",
            cancellationToken);

    public ValueTask<DurableOutputRetentionResult> PurgeDeadLettersAsync(
        DurableOutputRetentionRequest request,
        CancellationToken cancellationToken = default)
        => PurgeTerminalAsync(
            request,
            DeliveryDeadLettered,
            "dead_lettered_at_utc_ticks",
            "dead-letter retention",
            cancellationToken);

    private async ValueTask<DurableOutputRetentionResult> PurgeTerminalAsync(
        DurableOutputRetentionRequest request,
        int terminalState,
        string terminalTimestampColumn,
        string operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await EnsureDeliveryInitializedAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken)
                .ConfigureAwait(false);
            await using var transaction = BeginWriteTransaction(connection);
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"""
                WITH candidates AS (
                    SELECT application_address,
                           message_id
                    FROM fluxflow_durable_output_deliveries
                    WHERE state = $terminalState
                      AND {terminalTimestampColumn} IS NOT NULL
                      AND {terminalTimestampColumn} < $terminalBefore
                      AND ($address IS NULL
                           OR application_address = $address COLLATE BINARY)
                    ORDER BY {terminalTimestampColumn},
                             application_address COLLATE BINARY,
                             message_id COLLATE BINARY
                    LIMIT $maxCount
                )
                DELETE FROM fluxflow_durable_outputs
                WHERE EXISTS (
                    SELECT 1
                    FROM candidates
                    WHERE candidates.application_address =
                              fluxflow_durable_outputs.application_address COLLATE BINARY
                      AND candidates.message_id =
                              fluxflow_durable_outputs.message_id COLLATE BINARY);
                """;
            Add(command, "$terminalState", terminalState);
            Add(command, "$terminalBefore", request.TerminalBefore.UtcTicks);
            Add(command, "$address", request.Address?.Value);
            Add(command, "$maxCount", request.MaxCount);

            var deletedCount = await command.ExecuteNonQueryAsync(cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
            return new DurableOutputRetentionResult(deletedCount);
        }
        catch (SqliteException exception) when (IsBusy(exception))
        {
            throw CreateBusyException(operation, exception);
        }
    }
}
