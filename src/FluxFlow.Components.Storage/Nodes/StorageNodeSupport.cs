using System.Net.Sockets;
using System.Text.Json;
using FluxFlow.Components.Storage.Contracts;
using FluxFlow.Data;
using FluxFlow.Nodes;
using DataFlowError = FluxFlow.Data.FlowError;

namespace FluxFlow.Components.Storage.Nodes;

internal static class StorageNodeSupport
{
    public static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static Dictionary<string, string> CopyAttributes(
        Dictionary<string, string>? source)
        => source is null
            ? []
            : new Dictionary<string, string>(source, StringComparer.Ordinal);

    public static string ResolveCollection(
        string nodeType,
        string? requestCollection,
        string? defaultCollection)
    {
        var collection = Normalize(requestCollection) ?? Normalize(defaultCollection);
        if (collection is null)
        {
            throw new InvalidOperationException(
                $"{nodeType} requires a collection on the request or node options.");
        }

        return collection;
    }

    public static string ResolveKey(string nodeType, string? key)
    {
        var normalized = Normalize(key);
        if (normalized is null)
        {
            throw new InvalidOperationException($"{nodeType} request key cannot be empty.");
        }

        return normalized;
    }

    public static StorageWriteMode ResolveWriteMode(
        string nodeType,
        StorageWriteMode? requestMode,
        StorageWriteMode defaultMode)
    {
        var mode = requestMode ?? defaultMode;
        if (!Enum.IsDefined(mode))
        {
            throw new InvalidOperationException(
                $"{nodeType} request write mode '{mode}' is not supported.");
        }

        return mode;
    }

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
        CorrelationId? correlationId)
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
            Key = ResolveKey("storage.put", input.Key),
            Value = StorageContentEnvelopeCodec.Encode(input.Content),
            ContentType = input.Content.ContentType,
            Attributes = new Dictionary<string, string>(input.Attributes, StringComparer.Ordinal),
            ExpectedVersion = input.ExpectedVersion,
            ExpiresAt = input.ExpiresAt,
            CorrelationId = correlationId?.Value,
            Mode = mode
        };
    }

    public static StorageGetRequest NormalizeGet(
        StorageGetRequest input,
        string? defaultCollection,
        bool includeExpired)
        => input with
        {
            Collection = ResolveCollection(
                "storage.get",
                input.Collection,
                defaultCollection),
            Key = ResolveKey("storage.get", input.Key),
            IncludeExpired = input.IncludeExpired ?? includeExpired,
            CorrelationId = Normalize(input.CorrelationId)
        };

    public static StorageDeleteRequest NormalizeDelete(
        StorageDeleteRequest input,
        string? defaultCollection)
        => input with
        {
            Collection = ResolveCollection(
                "storage.delete",
                input.Collection,
                defaultCollection),
            Key = ResolveKey("storage.delete", input.Key),
            CorrelationId = Normalize(input.CorrelationId)
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
            Collection = ResolveCollection(
                "storage.query",
                input.Collection,
                defaultCollection),
            KeyPrefix = Normalize(input.KeyPrefix),
            Attributes = CopyAttributes(input.Attributes),
            IncludeExpired = input.IncludeExpired ?? includeExpired,
            Offset = input.Offset ?? offset,
            Limit = input.Limit ?? limit,
            CorrelationId = Normalize(input.CorrelationId)
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
            details: JsonSerializer.SerializeToElement(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["operation"] = operation,
                ["collection"] = collection,
                ["key"] = key,
                ["exceptionType"] = exception.GetType().FullName ?? exception.GetType().Name
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

}
