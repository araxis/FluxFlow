using System.Text.Json;
using FluxFlow.Components.Sessions.Contracts;
using FluxFlow.Data;

namespace FluxFlow.Components.Sessions.Nodes;

internal static class SessionContentRecordMapper
{
    public static SessionContentRecord Decode(SessionRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        try
        {
            var content = record.Payload switch
            {
                FlowContent typed => typed,
                JsonElement element => element.Deserialize<FlowContent>()
                    ?? throw new JsonException("Stored session content cannot be null."),
                _ => throw new InvalidOperationException(
                    "Stored payload is not canonical flow content.")
            };

            return new SessionContentRecord
            {
                SessionId = record.SessionId,
                Sequence = record.Sequence,
                Timestamp = record.Timestamp,
                Type = record.Type,
                Name = record.Name,
                Content = content,
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
}
