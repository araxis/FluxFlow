using System.Text.Json;
using FluxFlow.Components.Sessions.Contracts;
using FluxFlow.Data;

namespace FluxFlow.Components.Sessions.Nodes;

internal static class SessionContentEnvelopeCodec
{
    private const int CurrentFormatVersion = 1;

    public static object Encode(FlowContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (!content.HasOriginalRepresentation)
        {
            throw new SessionContentOperationException(
                SessionErrorCodeNames.ContentUnavailable,
                "session.recorder requires FlowContent with original bytes.");
        }

        return new StoredContentEnvelope
        {
            FormatVersion = CurrentFormatVersion,
            Bytes = Convert.ToBase64String(content.OriginalBytes.AsSpan()),
            ContentType = content.ContentType,
            Encoding = content.Encoding
        };
    }

    public static SessionContentRecord Decode(SessionRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        try
        {
            var envelope = record.Payload switch
            {
                StoredContentEnvelope typed => typed,
                JsonElement element => DecodeElement(element),
                _ => throw new InvalidOperationException(
                    "Stored payload is not a canonical session content envelope.")
            };

            if (envelope.FormatVersion != CurrentFormatVersion)
            {
                throw new InvalidOperationException(
                    $"Stored session content format version '{envelope.FormatVersion}' is not supported.");
            }

            return new SessionContentRecord
            {
                SessionId = record.SessionId,
                Sequence = record.Sequence,
                Timestamp = record.Timestamp,
                Type = record.Type,
                Name = record.Name,
                Content = FlowContent.FromBytes(
                    Convert.FromBase64String(envelope.Bytes),
                    envelope.ContentType,
                    envelope.Encoding),
                Attributes = record.Attributes
            };
        }
        catch (SessionContentOperationException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or FormatException or JsonException)
        {
            throw new SessionContentOperationException(
                SessionErrorCodeNames.StoredContentInvalid,
                $"Session record '{record.SessionId}/{record.Sequence}' does not contain valid canonical content.",
                innerException: exception);
        }
    }

    private static StoredContentEnvelope DecodeElement(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException(
                "Stored session content envelope must be a JSON object.");
        }

        return new StoredContentEnvelope
        {
            FormatVersion = GetRequiredProperty(element, "formatVersion").GetInt32(),
            Bytes = GetRequiredProperty(element, "bytes").GetString()
                ?? throw new InvalidOperationException(
                    "Stored session content envelope bytes cannot be null."),
            ContentType = GetOptionalString(element, "contentType"),
            Encoding = GetOptionalString(element, "encoding")
        };
    }

    private static JsonElement GetRequiredProperty(JsonElement element, string name)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (StringComparer.OrdinalIgnoreCase.Equals(property.Name, name))
                return property.Value;
        }

        throw new InvalidOperationException(
            $"Stored session content envelope is missing '{name}'.");
    }

    private static string? GetOptionalString(JsonElement element, string name)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (!StringComparer.OrdinalIgnoreCase.Equals(property.Name, name))
                continue;
            return property.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
                ? null
                : property.Value.GetString();
        }

        return null;
    }

    private sealed record StoredContentEnvelope
    {
        public required int FormatVersion { get; init; }

        public required string Bytes { get; init; }

        public string? ContentType { get; init; }

        public string? Encoding { get; init; }
    }
}
