using Microsoft.Data.Sqlite;

namespace FluxFlow.Engine.DurableInput.SqlFile;

public sealed partial class SqlFileDurableInputStore
{
    public ValueTask<DurableInputRetentionResult> PurgeDeliveredAsync(
        DurableInputRetentionRequest request,
        CancellationToken cancellationToken = default)
        => PurgeTerminalAsync(
            request,
            DurableInputState.Delivered,
            "delivered_at_utc_ticks",
            "delivered retention",
            cancellationToken);

    public ValueTask<DurableInputRetentionResult> PurgeDeadLettersAsync(
        DurableInputRetentionRequest request,
        CancellationToken cancellationToken = default)
        => PurgeTerminalAsync(
            request,
            DurableInputState.DeadLettered,
            "dead_lettered_at_utc_ticks",
            "dead-letter retention",
            cancellationToken);

    private async ValueTask<DurableInputRetentionResult> PurgeTerminalAsync(
        DurableInputRetentionRequest request,
        DurableInputState terminalState,
        string terminalTimestampColumn,
        string operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

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
                    FROM fluxflow_durable_inputs
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
                DELETE FROM fluxflow_durable_inputs
                WHERE EXISTS (
                    SELECT 1
                    FROM candidates
                    WHERE candidates.application_address =
                              fluxflow_durable_inputs.application_address COLLATE BINARY
                      AND candidates.message_id =
                              fluxflow_durable_inputs.message_id COLLATE BINARY)
                  AND state = $terminalState
                  AND {terminalTimestampColumn} IS NOT NULL
                  AND {terminalTimestampColumn} < $terminalBefore;
                """;
            Add(command, "$terminalState", (int)terminalState);
            Add(command, "$terminalBefore", request.TerminalBefore.UtcTicks);
            Add(command, "$address", request.Address?.Value);
            Add(command, "$maxCount", request.MaxCount);

            var deletedCount = await command.ExecuteNonQueryAsync(cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
            return new DurableInputRetentionResult(deletedCount);
        }
        catch (SqliteException exception) when (IsBusy(exception))
        {
            throw CreateBusyException(operation, exception);
        }
    }
}
