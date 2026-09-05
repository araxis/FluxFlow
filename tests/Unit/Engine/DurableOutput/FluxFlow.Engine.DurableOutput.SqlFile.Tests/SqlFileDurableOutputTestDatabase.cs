using System.Text.Json;
using FluxFlow.Composition.Addressing;
using FluxFlow.Data;
using FluxFlow.Nodes;
using Microsoft.Data.Sqlite;

namespace FluxFlow.Engine.DurableOutput.SqlFile.Tests;

internal static class SqlFileDurableOutputTestDatabase
{
    private const string OutputColumns = """
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
        captured_at_utc_ticks,
        captured_at_offset_minutes,
        headers_json
        """;

    public static async ValueTask<SqliteConnection> OpenAsync(
        string path,
        SqliteOpenMode mode = SqliteOpenMode.ReadWrite)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = mode,
            Pooling = false
        }.ToString());
        await connection.OpenAsync();
        return connection;
    }

    public static async ValueTask<DurableOutputEnvelope?> ReadOutputAsync(
        string path,
        DurableOutputKey key,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
            return null;

        await using var connection = await OpenAsync(path);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT {OutputColumns}
            FROM fluxflow_durable_outputs
            WHERE application_address = $address AND message_id = $messageId;
            """;
        command.Parameters.AddWithValue("$address", key.Address.Value);
        command.Parameters.AddWithValue("$messageId", key.MessageId.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        var address = ApplicationAddress.Parse(reader.GetString(0));
        var messageId = new MessageId(reader.GetString(1));
        var isError = reader.GetInt32(4) == 1;
        var payload = Parse(reader.GetString(5));
        FlowError? error = null;
        if (isError)
        {
            error = new FlowError(
                reader.GetString(6),
                reader.GetString(7),
                reader.GetString(8),
                reader.GetInt32(9) == 1,
                reader.IsDBNull(10) ? null : Parse(reader.GetString(10)));
        }

        var headers = JsonSerializer.Deserialize<Dictionary<string, string>>(
            reader.GetString(18))!;
        var envelope = new DurableOutputEnvelope(
            address,
            reader.GetString(2),
            isError,
            payload,
            error,
            messageId,
            new TraceId(reader.GetString(11)),
            FromStoredTime(reader.GetInt64(14), reader.GetInt32(15)),
            FromStoredTime(reader.GetInt64(16), reader.GetInt32(17)),
            reader.IsDBNull(12) ? null : new CorrelationId(reader.GetString(12)),
            reader.IsDBNull(13) ? null : new MessageId(reader.GetString(13)),
            headers,
            reader.GetInt32(3));
        if (await reader.ReadAsync(cancellationToken))
            throw new InvalidDataException($"Duplicate durable-output rows exist for key '{key}'.");
        return envelope;
    }

    public static async ValueTask<T> ScalarAsync<T>(
        SqliteConnection connection,
        string commandText)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        var result = await command.ExecuteScalarAsync();
        return (T)Convert.ChangeType(result!, typeof(T));
    }

    public static async ValueTask<IReadOnlyList<string>> ReadStringsAsync(
        SqliteConnection connection,
        string commandText)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        await using var reader = await command.ExecuteReaderAsync();
        var values = new List<string>();
        while (await reader.ReadAsync())
            values.Add(reader.GetString(0));
        return values;
    }

    public static async ValueTask ExecuteAsync(string path, string commandText)
    {
        await using var connection = await OpenAsync(path, SqliteOpenMode.ReadWriteCreate);
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync();
    }

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static DateTimeOffset FromStoredTime(long utcTicks, int offsetMinutes)
        => new DateTimeOffset(utcTicks, TimeSpan.Zero).ToOffset(TimeSpan.FromMinutes(offsetMinutes));
}
