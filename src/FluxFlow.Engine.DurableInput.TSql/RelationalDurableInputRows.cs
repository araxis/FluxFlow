using System.Buffers;
using System.Data;
using System.Text;
using System.Text.Json;
using FluxFlow.Composition.Addressing;
using FluxFlow.Data;
using FluxFlow.Nodes;
using Microsoft.Data.SqlClient;

namespace FluxFlow.Engine.DurableInput.TSql;

internal static class RelationalDurableInputRows
{
    internal const int AddressMaxLength = 300;
    internal const int MessageIdMaxLength = 128;
    internal const int ContractNameMaxLength = 1024;
    internal const int LeaseOwnerMaxLength = 512;
    internal const int LineageIdMaxLength = 512;
    internal const int EnvelopeColumnCount = 19;

    internal const string EnvelopeColumns = """
        i.application_address,
        i.message_id,
        i.contract_name,
        i.envelope_schema_version,
        i.is_error,
        i.payload_json,
        i.error_code,
        i.error_message,
        i.error_category,
        i.error_is_transient,
        i.error_details_json,
        i.trace_id,
        i.correlation_id,
        i.causation_id,
        i.message_timestamp_utc_ticks,
        i.message_timestamp_offset_minutes,
        i.enqueued_at_utc_ticks,
        i.enqueued_at_offset_minutes,
        i.headers_json
        """;

    internal static DurableInputEnvelope ReadEnvelope(SqlDataReader reader, int start = 0)
    {
        DurableInputKey? key = null;
        try
        {
            var address = ApplicationAddress.Parse(reader.GetString(start));
            var messageId = new MessageId(reader.GetString(start + 1));
            key = new DurableInputKey(address, messageId);
            var isError = reader.GetBoolean(start + 4);
            var payload = ParseJson(reader.GetString(start + 5), key.Value, "payload");
            FlowError? error = null;
            if (isError)
            {
                if (reader.IsDBNull(start + 6) || reader.IsDBNull(start + 7) ||
                    reader.IsDBNull(start + 8) || reader.IsDBNull(start + 9))
                {
                    throw Corrupt(key.Value, "error fields are incomplete");
                }

                error = new FlowError(
                    reader.GetString(start + 6),
                    reader.GetString(start + 7),
                    reader.GetString(start + 8),
                    reader.GetBoolean(start + 9),
                    reader.IsDBNull(start + 10)
                        ? null
                        : ParseJson(reader.GetString(start + 10), key.Value, "error details"));
            }
            else if (!reader.IsDBNull(start + 6) || !reader.IsDBNull(start + 7) ||
                     !reader.IsDBNull(start + 8) || !reader.IsDBNull(start + 9) ||
                     !reader.IsDBNull(start + 10))
            {
                throw Corrupt(key.Value, "value row contains error fields");
            }

            var correlationId = reader.IsDBNull(start + 12)
                ? (CorrelationId?)null
                : new CorrelationId(reader.GetString(start + 12));
            var causationId = reader.IsDBNull(start + 13)
                ? (MessageId?)null
                : new MessageId(reader.GetString(start + 13));

            return new DurableInputEnvelope(
                address,
                reader.GetString(start + 2),
                isError,
                payload,
                error,
                messageId,
                new TraceId(reader.GetString(start + 11)),
                FromStoredTime(
                    reader.GetInt64(start + 14),
                    reader.GetInt16(start + 15),
                    key.Value,
                    "message timestamp"),
                FromStoredTime(
                    reader.GetInt64(start + 16),
                    reader.GetInt16(start + 17),
                    key.Value,
                    "enqueue timestamp"),
                correlationId,
                causationId,
                ReadHeaders(reader.GetString(start + 18), key.Value),
                reader.GetInt32(start + 3));
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (exception is
            ArgumentException or FormatException or InvalidCastException or
            InvalidOperationException or JsonException or OverflowException)
        {
            throw new InvalidDataException(
                key is null
                    ? "Relational durable-input row contains an invalid key."
                    : $"Relational durable-input row '{key}' is corrupt.",
                exception);
        }
    }

    internal static void AddEnvelopeParameters(
        SqlCommand command,
        DurableInputEnvelope envelope)
    {
        AddKey(command, envelope.Key);
        AddNVarChar(command, "@contractName", envelope.ContractName, ContractNameMaxLength);
        Add(command, "@schemaVersion", SqlDbType.Int, envelope.SchemaVersion);
        Add(command, "@isError", SqlDbType.Bit, envelope.IsError);
        AddNVarChar(command, "@payloadJson", envelope.Payload.GetRawText(), -1);
        AddNVarChar(command, "@errorCode", envelope.Error?.Code, ContractNameMaxLength);
        AddNVarChar(command, "@errorMessage", envelope.Error?.Message, -1);
        AddNVarChar(command, "@errorCategory", envelope.Error?.Category, ContractNameMaxLength);
        Add(command, "@errorIsTransient", SqlDbType.Bit, envelope.Error?.IsTransient);
        AddNVarChar(command, "@errorDetailsJson", envelope.Error?.Details?.GetRawText(), -1);
        AddNVarChar(command, "@traceId", envelope.TraceId.Value, LineageIdMaxLength);
        AddNVarChar(command, "@correlationId", envelope.CorrelationId?.Value, LineageIdMaxLength);
        AddNVarChar(command, "@causationId", envelope.CausationId?.Value, LineageIdMaxLength);
        Add(command, "@messageTimestamp", SqlDbType.BigInt, envelope.Timestamp.UtcTicks);
        Add(command, "@messageTimestampOffset", SqlDbType.SmallInt, ToOffsetMinutes(envelope.Timestamp));
        Add(command, "@enqueuedAt", SqlDbType.BigInt, envelope.EnqueuedAt.UtcTicks);
        Add(command, "@enqueuedAtOffset", SqlDbType.SmallInt, ToOffsetMinutes(envelope.EnqueuedAt));
        AddNVarChar(command, "@headersJson", SerializeHeaders(envelope.Headers), -1);
    }

    internal static void ValidateEnvelope(DurableInputEnvelope envelope)
    {
        ValidateKey(envelope.Key);
        ValidateRequiredLength(
            envelope.ContractName,
            ContractNameMaxLength,
            nameof(envelope),
            "contract name");
        ValidateRequiredLength(
            envelope.TraceId.Value,
            LineageIdMaxLength,
            nameof(envelope),
            "trace identifier");
        ValidateOptionalLength(
            envelope.CorrelationId?.Value,
            LineageIdMaxLength,
            nameof(envelope),
            "correlation identifier");
        ValidateOptionalLength(
            envelope.CausationId?.Value,
            LineageIdMaxLength,
            nameof(envelope),
            "causation identifier");
        ValidateOptionalLength(
            envelope.Error?.Code,
            ContractNameMaxLength,
            nameof(envelope),
            "error code");
        ValidateOptionalLength(
            envelope.Error?.Category,
            ContractNameMaxLength,
            nameof(envelope),
            "error category");
    }

    internal static void ValidateKey(DurableInputKey key)
    {
        if (key.Address is null || key.MessageId.IsEmpty)
            throw new ArgumentException("Durable input key must contain an address and message id.", nameof(key));

        ValidateRequiredLength(
            key.Address.Value,
            AddressMaxLength,
            nameof(key),
            "application address");
        ValidateRequiredLength(
            key.MessageId.Value,
            MessageIdMaxLength,
            nameof(key),
            "message identifier");
    }

    internal static void ValidateLeaseRequest(DurableInputLeaseRequest request)
        => ValidateRequiredLength(
            request.OwnerId,
            LeaseOwnerMaxLength,
            nameof(request),
            "lease owner");

    internal static void ValidateAddress(string value, string parameterName)
        => ValidateRequiredLength(
            value,
            AddressMaxLength,
            parameterName,
            "application address");

    internal static void AddKey(SqlCommand command, DurableInputKey key)
    {
        AddNVarChar(command, "@address", key.Address.Value, AddressMaxLength);
        AddNVarChar(command, "@messageId", key.MessageId.Value, MessageIdMaxLength);
    }

    internal static SqlParameter AddNVarChar(
        SqlCommand command,
        string name,
        string? value,
        int size)
    {
        var parameter = command.Parameters.Add(name, SqlDbType.NVarChar, size);
        parameter.Value = value is null ? DBNull.Value : value;
        return parameter;
    }

    internal static SqlParameter Add(
        SqlCommand command,
        string name,
        SqlDbType type,
        object? value)
    {
        var parameter = command.Parameters.Add(name, type);
        parameter.Value = value ?? DBNull.Value;
        return parameter;
    }

    internal static DateTimeOffset ReadStoredTime(
        SqlDataReader reader,
        int ticksOrdinal,
        int offsetOrdinal,
        DurableInputKey key,
        string field)
    {
        if (reader.IsDBNull(ticksOrdinal) || reader.IsDBNull(offsetOrdinal))
            throw Corrupt(key, $"{field} is incomplete");
        return FromStoredTime(
            reader.GetInt64(ticksOrdinal),
            reader.GetInt16(offsetOrdinal),
            key,
            field);
    }

    internal static DateTimeOffset ReadUtcTime(
        SqlDataReader reader,
        int ordinal,
        DurableInputKey key,
        string field)
    {
        if (reader.IsDBNull(ordinal))
            throw Corrupt(key, $"{field} is missing");
        return FromStoredTime(reader.GetInt64(ordinal), 0, key, field);
    }

    internal static DateTimeOffset FromStoredTime(
        long utcTicks,
        int offsetMinutes,
        DurableInputKey key,
        string field)
    {
        try
        {
            return new DateTimeOffset(utcTicks, TimeSpan.Zero)
                .ToOffset(TimeSpan.FromMinutes(offsetMinutes));
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new InvalidDataException(
                $"Relational durable-input row '{key}' contains an invalid {field}.",
                exception);
        }
    }

    internal static int ReadPositiveInt32(
        SqlDataReader reader,
        int ordinal,
        DurableInputKey key,
        string field)
    {
        var value = reader.GetInt32(ordinal);
        return value > 0 ? value : throw Corrupt(key, $"{field} must be positive");
    }

    internal static long ReadPositiveInt64(
        SqlDataReader reader,
        int ordinal,
        DurableInputKey key,
        string field)
    {
        var value = reader.GetInt64(ordinal);
        return value > 0 ? value : throw Corrupt(key, $"{field} must be positive");
    }

    internal static DurableInputFailureKind ReadFailureKind(
        SqlDataReader reader,
        int ordinal,
        DurableInputKey key)
    {
        if (reader.IsDBNull(ordinal))
            throw Corrupt(key, "dead-letter failure kind is missing");

        var value = reader.GetInt32(ordinal);
        if (!Enum.IsDefined(typeof(DurableInputFailureKind), value))
            throw Corrupt(key, $"failure kind value {value} is invalid");
        return (DurableInputFailureKind)value;
    }

    internal static DurableInputFailure ReadFailure(
        SqlDataReader reader,
        int kindOrdinal,
        int descriptionOrdinal,
        DurableInputKey key)
    {
        if (reader.IsDBNull(kindOrdinal) || reader.IsDBNull(descriptionOrdinal))
            throw Corrupt(key, "dead-letter failure fields are incomplete");

        return new DurableInputFailure(
            ReadFailureKind(reader, kindOrdinal, key),
            reader.GetString(descriptionOrdinal));
    }

    internal static short ToOffsetMinutes(DateTimeOffset value)
        => checked((short)value.Offset.TotalMinutes);

    internal static InvalidDataException Corrupt(DurableInputKey key, string detail)
        => new($"Relational durable-input row '{key}' is corrupt: {detail}.");

    private static void ValidateRequiredLength(
        string? value,
        int maximumLength,
        string parameterName,
        string field)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"T-SQL durable input {field} is required.", parameterName);
        if (value.Length > maximumLength)
        {
            throw new ArgumentException(
                $"T-SQL durable input {field} cannot exceed {maximumLength} characters.",
                parameterName);
        }
    }

    private static void ValidateOptionalLength(
        string? value,
        int maximumLength,
        string parameterName,
        string field)
    {
        if (value is not null && value.Length > maximumLength)
        {
            throw new ArgumentException(
                $"T-SQL durable input {field} cannot exceed {maximumLength} characters.",
                parameterName);
        }
    }

    private static JsonElement ParseJson(
        string json,
        DurableInputKey key,
        string field)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                $"Relational durable-input row '{key}' contains invalid {field} JSON.",
                exception);
        }
    }

    private static IReadOnlyDictionary<string, string> ReadHeaders(
        string json,
        DurableInputKey key)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw Corrupt(key, "headers JSON must be an object");

            var headers = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.String)
                    throw Corrupt(key, $"header '{property.Name}' must contain a string value");
                headers.Add(property.Name, property.Value.GetString()!);
            }

            return headers;
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            throw new InvalidDataException(
                $"Relational durable-input row '{key}' contains invalid headers JSON.",
                exception);
        }
    }

    private static string SerializeHeaders(IReadOnlyDictionary<string, string> headers)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            foreach (var header in headers.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
                writer.WriteString(header.Key, header.Value);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }
}
