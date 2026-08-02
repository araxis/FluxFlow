using System.Text.Json;
using FluxFlow.Components.Storage.Contracts;
using FluxFlow.Data;

namespace FluxFlow.Components.Storage.Nodes;

internal static class StorageContentRecordMapper
{
    public static StorageContentRecord Decode(StorageRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        try
        {
            var content = record.Value switch
            {
                FlowContent typed => typed,
                JsonElement element => element.Deserialize<FlowContent>()
                    ?? throw new JsonException("Stored content cannot be null."),
                _ => throw new InvalidOperationException(
                    "Stored value is not canonical flow content.")
            };

            return new StorageContentRecord
            {
                Collection = record.Collection,
                Key = record.Key,
                Content = content,
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
}
