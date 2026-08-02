using Microsoft.Data.Sqlite;

namespace FluxFlow.Engine.DurableInput.SqlFile.Tests;

internal static class SqlFileVersionOneDatabase
{
    public static async ValueTask DowngradeAsync(string databasePath)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false
        }.ToString());
        await connection.OpenAsync();
        await using var transaction = connection.BeginTransaction(deferred: false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DROP INDEX ix_fluxflow_durable_inputs_pending_due;
            DROP INDEX ix_fluxflow_durable_inputs_lease_expiry;
            DROP INDEX ix_fluxflow_durable_inputs_dead_lettered;

            ALTER TABLE fluxflow_durable_inputs RENAME TO fluxflow_durable_inputs_v2;

            CREATE TABLE fluxflow_durable_inputs (
                application_address TEXT COLLATE BINARY NOT NULL,
                message_id TEXT COLLATE BINARY NOT NULL,
                contract_name TEXT COLLATE BINARY NOT NULL CHECK (length(contract_name) > 0),
                envelope_schema_version INTEGER NOT NULL CHECK (envelope_schema_version > 0),
                is_error INTEGER NOT NULL CHECK (is_error IN (0, 1)),
                payload_json TEXT NOT NULL,
                error_code TEXT NULL,
                error_message TEXT NULL,
                error_category TEXT NULL,
                error_is_transient INTEGER NULL CHECK (error_is_transient IS NULL OR error_is_transient IN (0, 1)),
                error_details_json TEXT NULL,
                trace_id TEXT NOT NULL,
                correlation_id TEXT NULL,
                causation_id TEXT NULL,
                message_timestamp_utc_ticks INTEGER NOT NULL,
                message_timestamp_offset_minutes INTEGER NOT NULL
                    CHECK (message_timestamp_offset_minutes BETWEEN -840 AND 840),
                enqueued_at_utc_ticks INTEGER NOT NULL,
                enqueued_at_offset_minutes INTEGER NOT NULL
                    CHECK (enqueued_at_offset_minutes BETWEEN -840 AND 840),
                headers_json TEXT NOT NULL,
                state INTEGER NOT NULL CHECK (state IN (0, 1, 2, 3)),
                attempt INTEGER NOT NULL CHECK (attempt >= 0),
                next_attempt_utc_ticks INTEGER NULL,
                lease_owner TEXT NULL,
                lease_token TEXT NULL,
                leased_at_utc_ticks INTEGER NULL,
                lease_until_utc_ticks INTEGER NULL,
                failure_kind INTEGER NULL,
                failure_description TEXT NULL,
                delivered_at_utc_ticks INTEGER NULL,
                dead_lettered_at_utc_ticks INTEGER NULL,
                PRIMARY KEY (application_address, message_id),
                CHECK ((is_error = 0
                        AND error_code IS NULL
                        AND error_message IS NULL
                        AND error_category IS NULL
                        AND error_is_transient IS NULL
                        AND error_details_json IS NULL)
                    OR (is_error = 1
                        AND error_code IS NOT NULL
                        AND error_message IS NOT NULL
                        AND error_category IS NOT NULL
                        AND error_is_transient IS NOT NULL)),
                CHECK ((failure_kind IS NULL AND failure_description IS NULL)
                    OR (failure_kind IS NOT NULL AND failure_description IS NOT NULL)),
                CHECK ((state = 0
                        AND next_attempt_utc_ticks IS NOT NULL
                        AND lease_owner IS NULL
                        AND lease_token IS NULL
                        AND leased_at_utc_ticks IS NULL
                        AND lease_until_utc_ticks IS NULL
                        AND delivered_at_utc_ticks IS NULL
                        AND dead_lettered_at_utc_ticks IS NULL)
                    OR (state = 1
                        AND attempt > 0
                        AND next_attempt_utc_ticks IS NULL
                        AND lease_owner IS NOT NULL
                        AND lease_token IS NOT NULL
                        AND leased_at_utc_ticks IS NOT NULL
                        AND lease_until_utc_ticks IS NOT NULL
                        AND lease_until_utc_ticks > leased_at_utc_ticks
                        AND delivered_at_utc_ticks IS NULL
                        AND dead_lettered_at_utc_ticks IS NULL)
                    OR (state = 2
                        AND next_attempt_utc_ticks IS NULL
                        AND lease_owner IS NULL
                        AND lease_token IS NULL
                        AND leased_at_utc_ticks IS NULL
                        AND lease_until_utc_ticks IS NULL
                        AND delivered_at_utc_ticks IS NOT NULL
                        AND dead_lettered_at_utc_ticks IS NULL)
                    OR (state = 3
                        AND next_attempt_utc_ticks IS NULL
                        AND lease_owner IS NULL
                        AND lease_token IS NULL
                        AND leased_at_utc_ticks IS NULL
                        AND lease_until_utc_ticks IS NULL
                        AND failure_kind IS NOT NULL
                        AND delivered_at_utc_ticks IS NULL
                        AND dead_lettered_at_utc_ticks IS NOT NULL))
            ) WITHOUT ROWID;

            INSERT INTO fluxflow_durable_inputs (
                application_address,
                message_id,
                contract_name,
                envelope_schema_version,
                is_error,
                payload_json,
                error_code,
                error_message,
                error_category,
                error_is_transient,
                error_details_json,
                trace_id,
                correlation_id,
                causation_id,
                message_timestamp_utc_ticks,
                message_timestamp_offset_minutes,
                enqueued_at_utc_ticks,
                enqueued_at_offset_minutes,
                headers_json,
                state,
                attempt,
                next_attempt_utc_ticks,
                lease_owner,
                lease_token,
                leased_at_utc_ticks,
                lease_until_utc_ticks,
                failure_kind,
                failure_description,
                delivered_at_utc_ticks,
                dead_lettered_at_utc_ticks)
            SELECT application_address,
                   message_id,
                   contract_name,
                   envelope_schema_version,
                   is_error,
                   payload_json,
                   error_code,
                   error_message,
                   error_category,
                   error_is_transient,
                   error_details_json,
                   trace_id,
                   correlation_id,
                   causation_id,
                   message_timestamp_utc_ticks,
                   message_timestamp_offset_minutes,
                   enqueued_at_utc_ticks,
                   enqueued_at_offset_minutes,
                   headers_json,
                   state,
                   attempt,
                   next_attempt_utc_ticks,
                   lease_owner,
                   lease_token,
                   leased_at_utc_ticks,
                   lease_until_utc_ticks,
                   failure_kind,
                   failure_description,
                   delivered_at_utc_ticks,
                   dead_lettered_at_utc_ticks
            FROM fluxflow_durable_inputs_v2;

            DROP TABLE fluxflow_durable_inputs_v2;

            CREATE INDEX ix_fluxflow_durable_inputs_pending_due
                ON fluxflow_durable_inputs (
                    state,
                    next_attempt_utc_ticks,
                    enqueued_at_utc_ticks,
                    application_address,
                    message_id)
                WHERE state = 0;

            CREATE INDEX ix_fluxflow_durable_inputs_lease_expiry
                ON fluxflow_durable_inputs (
                    state,
                    lease_until_utc_ticks,
                    enqueued_at_utc_ticks,
                    application_address,
                    message_id)
                WHERE state = 1;

            UPDATE fluxflow_durable_input_schema
            SET version = 1
            WHERE singleton = 1;
            """;
        await command.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
    }
}
