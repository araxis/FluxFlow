using System.Net.Sockets;
using FluxFlow.Components.Storage.Contracts;
using FluxFlow.Data;
using FluxFlow.Nodes;
using DataFlowError = FluxFlow.Data.FlowError;

namespace FluxFlow.Components.Storage.Nodes;

internal static class StorageContentNodeSupport
{
    internal static T NormalizeRequest<T>(string operation, Func<T> normalize)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        ArgumentNullException.ThrowIfNull(normalize);

        try
        {
            return normalize();
        }
        catch (StorageContentOperationException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException)
        {
            throw new StorageContentOperationException(
                StorageErrorCodeNames.InvalidRequest,
                $"storage.{operation} request is invalid: {exception.Message}",
                innerException: exception);
        }
    }

    public static StoragePutRequest CreatePutRequest(
        StorageContentPutRequest input,
        string collection,
        StorageWriteMode mode,
        CorrelationId correlationId)
    {
        if (input.Content is null)
        {
            throw new StorageContentOperationException(
                StorageErrorCodeNames.ContentMissing,
                "storage.put requires content.");
        }

        return new StoragePutRequest
        {
            Collection = collection,
            Key = StorageNodeSupport.ResolveKey("storage.put", input.Key),
            Value = StorageContentEnvelopeCodec.Encode(input.Content),
            ContentType = input.Content.ContentType,
            Attributes = new Dictionary<string, string>(input.Attributes, StringComparer.Ordinal),
            ExpectedVersion = input.ExpectedVersion,
            ExpiresAt = input.ExpiresAt,
            CorrelationId = correlationId.Value,
            Mode = mode
        };
    }

    public static StorageGetRequest NormalizeGet(
        StorageGetRequest input,
        string? defaultCollection,
        bool includeExpired)
        => input with
        {
            Collection = StorageNodeSupport.ResolveCollection(
                "storage.get",
                input.Collection,
                defaultCollection),
            Key = StorageNodeSupport.ResolveKey("storage.get", input.Key),
            IncludeExpired = input.IncludeExpired ?? includeExpired,
            CorrelationId = StorageNodeSupport.Normalize(input.CorrelationId)
        };

    public static StorageDeleteRequest NormalizeDelete(
        StorageDeleteRequest input,
        string? defaultCollection)
        => input with
        {
            Collection = StorageNodeSupport.ResolveCollection(
                "storage.delete",
                input.Collection,
                defaultCollection),
            Key = StorageNodeSupport.ResolveKey("storage.delete", input.Key),
            CorrelationId = StorageNodeSupport.Normalize(input.CorrelationId)
        };

    public static StorageQueryRequest NormalizeQuery(
        StorageQueryRequest input,
        string? defaultCollection,
        bool includeExpired,
        int offset,
        int limit)
    {
        var request = input with
        {
            Collection = StorageNodeSupport.ResolveCollection(
                "storage.query",
                input.Collection,
                defaultCollection),
            KeyPrefix = StorageNodeSupport.Normalize(input.KeyPrefix),
            Attributes = StorageNodeSupport.CopyAttributes(input.Attributes),
            IncludeExpired = input.IncludeExpired ?? includeExpired,
            Offset = input.Offset ?? offset,
            Limit = input.Limit ?? limit,
            CorrelationId = StorageNodeSupport.Normalize(input.CorrelationId)
        };
        StorageQueryMatcher.Validate(request);
        return request;
    }

    public static void ValidateIdentity(
        StorageRecord record,
        string collection,
        string key,
        string operation)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (!StringComparer.Ordinal.Equals(record.Collection, collection) ||
            !StringComparer.Ordinal.Equals(record.Key, key))
        {
            throw new StorageContentOperationException(
                StorageErrorCodeNames.StoredContentInvalid,
                $"storage.{operation} store returned a record for a different identity.");
        }
    }

    public static DataFlowError CreateError(
        string code,
        string message,
        string operation,
        string? collection,
        string? key,
        Exception exception)
        => new(
            code,
            message,
            category: "Storage",
            isTransient: IsTransient(exception),
            details: FlowValue.FromObject(new Dictionary<string, FlowValue>(StringComparer.Ordinal)
            {
                ["operation"] = FlowValue.From(operation),
                ["collection"] = OptionalValue(collection),
                ["key"] = OptionalValue(key),
                ["exceptionType"] = FlowValue.From(
                    exception.GetType().FullName ?? exception.GetType().Name)
            }));

    public static FlowEvent CreateEvent<TInput>(
        FlowMessage<TInput> message,
        DateTimeOffset timestamp,
        string name,
        FlowEventLevel level,
        string text,
        string resultKind,
        bool isError,
        string operation,
        string? collection,
        string? key,
        string? errorCode = null,
        int? count = null,
        long? version = null)
    {
        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["resultKind"] = resultKind,
            ["isError"] = isError,
            ["operation"] = operation,
            ["collection"] = collection,
            ["key"] = key
        };
        if (errorCode is not null)
            attributes["errorCode"] = errorCode;
        if (count.HasValue)
            attributes["count"] = count.Value;
        if (version.HasValue)
            attributes["version"] = version.Value;

        return new FlowEvent
        {
            Timestamp = timestamp,
            CorrelationId = message.CorrelationId,
            Name = name,
            Level = level,
            Message = text,
            Attributes = attributes
        };
    }

    public static (string Code, string Message) Classify(
        Exception exception,
        string operation,
        string operationFailureCode)
        => exception is StorageContentOperationException known
            ? (known.Code, known.Message)
            : (operationFailureCode, $"storage.{operation} failed: {exception.Message}");

    private static bool IsTransient(Exception exception)
        => exception is StorageContentOperationException { IsTransient: true } or
            IOException or TimeoutException or SocketException;

    private static FlowValue OptionalValue(string? value)
        => string.IsNullOrWhiteSpace(value) ? FlowValue.Null : FlowValue.From(value.Trim());
}
