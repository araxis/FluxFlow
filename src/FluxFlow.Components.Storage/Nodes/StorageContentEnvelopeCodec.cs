using System.Text.Json;
using FluxFlow.Components.Storage.Contracts;
using FluxFlow.Data;

namespace FluxFlow.Components.Storage.Nodes;

internal static class StorageContentEnvelopeCodec
{
    private const int CurrentFormatVersion = 1;

    public static object Encode(FlowContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return new StoredContentEnvelope
        {
            FormatVersion = CurrentFormatVersion,
            Bytes = Convert.ToBase64String(content.Bytes.AsSpan()),
            ContentType = content.ContentType,
            Encoding = content.Encoding
        };
    }

    public static StorageContentRecord Decode(StorageRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        StoredContentEnvelope envelope;
        try
        {
            envelope = record.Value switch
            {
                StoredContentEnvelope typed => typed,
                JsonElement element => DecodeElement(element),
                _ => throw new InvalidOperationException(
                    "Stored value is not a canonical content envelope.")
            };

            if (envelope.FormatVersion != CurrentFormatVersion)
            {
                throw new InvalidOperationException(
                    $"Stored content format version '{envelope.FormatVersion}' is not supported.");
            }

            var bytes = Convert.FromBase64String(envelope.Bytes);
            return new StorageContentRecord
            {
                Collection = record.Collection,
                Key = record.Key,
                Content = FlowContent.FromBytes(
                    bytes,
                    envelope.ContentType,
                    envelope.Encoding),
                Attributes = record.Attributes,
                Version = record.Version,
                StoredAt = record.StoredAt,
                ExpiresAt = record.ExpiresAt,
                CorrelationId = record.CorrelationId
            };
        }
        catch (StorageContentOperationException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or FormatException or JsonException)
        {
            throw new StorageContentOperationException(
                StorageErrorCodeNames.StoredContentInvalid,
                $"Storage record '{record.Collection}/{record.Key}' does not contain valid canonical content.",
                innerException: exception);
        }
    }

    private static StoredContentEnvelope DecodeElement(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException(
                "Stored content envelope must be a JSON object.");
        }

        return new StoredContentEnvelope
        {
            FormatVersion = GetRequiredProperty(element, "formatVersion").GetInt32(),
            Bytes = GetRequiredProperty(element, "bytes").GetString()
                ?? throw new InvalidOperationException(
                    "Stored content envelope bytes cannot be null."),
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
            $"Stored content envelope is missing '{name}'.");
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
