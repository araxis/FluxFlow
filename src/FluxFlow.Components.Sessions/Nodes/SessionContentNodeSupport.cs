using System.Net.Sockets;
using FluxFlow.Components.Sessions.Contracts;
using FluxFlow.Components.Sessions.Options;
using FluxFlow.Data;
using FluxFlow.Nodes;
using DataFlowError = FluxFlow.Data.FlowError;

namespace FluxFlow.Components.Sessions.Nodes;

internal static class SessionContentNodeSupport
{
    public static T NormalizeRequest<T>(string operation, Func<T> normalize)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        ArgumentNullException.ThrowIfNull(normalize);

        try
        {
            return normalize();
        }
        catch (SessionContentOperationException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException)
        {
            throw new SessionContentOperationException(
                SessionErrorCodeNames.InvalidRequest,
                $"session.{operation} request is invalid: {exception.Message}",
                innerException: exception);
        }
    }

    public static SessionRecordInput CreateRecordInput(SessionContentRecordInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Content is null)
        {
            throw new SessionContentOperationException(
                SessionErrorCodeNames.ContentMissing,
                "session.recorder requires content.");
        }

        return new SessionRecordInput
        {
            Timestamp = input.Timestamp,
            Type = input.Type,
            Name = input.Name,
            Payload = SessionContentEnvelopeCodec.Encode(input.Content),
            ContentType = input.Content.ContentType,
            Attributes = new Dictionary<string, string>(input.Attributes, StringComparer.Ordinal)
        };
    }

    public static SessionMetadata ValidateAndCopySession(
        SessionMetadata? session,
        string operation,
        string? expectedSessionId = null)
    {
        if (session is null)
        {
            throw new SessionContentOperationException(
                SessionErrorCodeNames.StoreUnavailable,
                $"session.{operation} store returned a null session.");
        }

        if (string.IsNullOrWhiteSpace(session.SessionId))
        {
            throw new SessionContentOperationException(
                SessionErrorCodeNames.StoredContentInvalid,
                $"session.{operation} store returned a session without an id.");
        }

        if (expectedSessionId is not null &&
            !StringComparer.Ordinal.Equals(session.SessionId, expectedSessionId))
        {
            throw new SessionContentOperationException(
                SessionErrorCodeNames.StoredContentInvalid,
                $"session.{operation} store returned a different session.");
        }

        return session with
        {
            Tags = new Dictionary<string, string>(session.Tags, StringComparer.Ordinal)
        };
    }

    public static SessionContentRecord ValidateAndDecodeRecord(
        SessionRecord? record,
        string operation,
        string expectedSessionId,
        long? expectedSequence = null)
    {
        if (record is null)
        {
            throw new SessionContentOperationException(
                SessionErrorCodeNames.StoreUnavailable,
                $"session.{operation} store returned a null record.");
        }

        if (!StringComparer.Ordinal.Equals(record.SessionId, expectedSessionId))
        {
            throw new SessionContentOperationException(
                SessionErrorCodeNames.StoredContentInvalid,
                $"session.{operation} store returned a record for a different session.");
        }

        if (expectedSequence.HasValue && record.Sequence != expectedSequence.Value)
        {
            throw new SessionContentOperationException(
                SessionErrorCodeNames.StoredContentInvalid,
                $"session.{operation} store returned a record with an unexpected sequence.");
        }

        return SessionContentEnvelopeCodec.Decode(record);
    }

    public static SessionQueryRequest NormalizeQuery(
        SessionQueryRequest input,
        SessionQueryOptions options)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(options);

        var limit = input.Limit ?? options.Limit;
        if (limit <= 0)
            throw new InvalidOperationException("session.query request limit must be greater than zero.");

        ValidateRange(input.StartedFrom, input.StartedTo, "startedFrom", "startedTo");
        ValidateRange(input.EndedFrom, input.EndedTo, "endedFrom", "endedTo");

        var includeActive = input.IncludeActive ?? options.IncludeActive;
        var includeCompleted = input.IncludeCompleted ?? options.IncludeCompleted;
        if (!includeActive && !includeCompleted)
        {
            throw new InvalidOperationException(
                "session.query must include active sessions, completed sessions, or both.");
        }

        return input with
        {
            Name = Normalize(input.Name) ?? Normalize(options.SessionName),
            NamePrefix = Normalize(input.NamePrefix) ?? Normalize(options.NamePrefix),
            Tags = MergeTags(options.Tags, input.Tags),
            IncludeActive = includeActive,
            IncludeCompleted = includeCompleted,
            Limit = limit,
            CorrelationId = Normalize(input.CorrelationId)
        };
    }

    public static IReadOnlyList<SessionMetadata> ValidateQuerySessions(
        SessionQueryRequest request,
        IReadOnlyList<SessionMetadata>? sessions)
    {
        if (sessions is null)
        {
            throw new SessionContentOperationException(
                SessionErrorCodeNames.StoreUnavailable,
                "session.query store returned a null result.");
        }

        if (sessions.Count > request.Limit!.Value)
        {
            throw new SessionContentOperationException(
                SessionErrorCodeNames.StoredContentInvalid,
                "session.query store returned more sessions than requested.");
        }

        return sessions.Select(session => ValidateQuerySession(request, session)).ToArray();
    }

    public static DataFlowError CreateError(
        string code,
        string message,
        string operation,
        string? sessionId,
        Exception exception,
        long? sequence = null)
    {
        var details = new Dictionary<string, FlowValue>(StringComparer.Ordinal)
        {
            ["operation"] = FlowValue.From(operation),
            ["sessionId"] = OptionalValue(sessionId),
            ["exceptionType"] = FlowValue.From(
                exception.GetType().FullName ?? exception.GetType().Name)
        };
        if (sequence.HasValue)
            details["sequence"] = FlowValue.From(sequence.Value);

        return new DataFlowError(
            code,
            message,
            category: "Sessions",
            isTransient: IsTransient(exception),
            details: FlowValue.FromObject(details));
    }

    public static FlowEvent CreateEvent(
        DateTimeOffset timestamp,
        string name,
        FlowEventLevel level,
        string message,
        string resultKind,
        bool isError,
        string operation,
        string? sessionId,
        CorrelationId? correlationId = null,
        string? errorCode = null,
        long? sequence = null,
        int? count = null)
    {
        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["resultKind"] = resultKind,
            ["isError"] = isError,
            ["operation"] = operation,
            ["sessionId"] = sessionId
        };
        if (errorCode is not null)
            attributes["errorCode"] = errorCode;
        if (sequence.HasValue)
            attributes["sequence"] = sequence.Value;
        if (count.HasValue)
            attributes["count"] = count.Value;

        return new FlowEvent
        {
            Timestamp = timestamp,
            CorrelationId = correlationId,
            Name = name,
            Level = level,
            Message = message,
            Attributes = attributes
        };
    }

    public static (string Code, string Message) Classify(
        Exception exception,
        string operation,
        string fallbackCode)
        => exception is SessionContentOperationException known
            ? (known.Code, known.Message)
            : (fallbackCode, $"session.{operation} failed: {exception.Message}");

    private static SessionMetadata ValidateQuerySession(
        SessionQueryRequest request,
        SessionMetadata? session)
    {
        var copy = ValidateAndCopySession(session, "query");
        if (!string.IsNullOrWhiteSpace(request.Name) &&
            !StringComparer.Ordinal.Equals(copy.Name, request.Name))
            ThrowStoreFilterViolation("name");
        if (!string.IsNullOrWhiteSpace(request.NamePrefix) &&
            copy.Name?.StartsWith(request.NamePrefix, StringComparison.Ordinal) != true)
            ThrowStoreFilterViolation("namePrefix");

        foreach (var (key, value) in request.Tags)
        {
            if (!copy.Tags.TryGetValue(key, out var actual) ||
                !StringComparer.Ordinal.Equals(actual, value))
                ThrowStoreFilterViolation($"tag '{key}'");
        }

        if (request.StartedFrom.HasValue && copy.StartedAt < request.StartedFrom.Value)
            ThrowStoreFilterViolation("startedFrom");
        if (request.StartedTo.HasValue && copy.StartedAt > request.StartedTo.Value)
            ThrowStoreFilterViolation("startedTo");
        if (request.EndedFrom.HasValue &&
            (copy.EndedAt is null || copy.EndedAt.Value < request.EndedFrom.Value))
            ThrowStoreFilterViolation("endedFrom");
        if (request.EndedTo.HasValue &&
            (copy.EndedAt is null || copy.EndedAt.Value > request.EndedTo.Value))
            ThrowStoreFilterViolation("endedTo");
        if (request.IncludeActive == false && copy.EndedAt is null)
            ThrowStoreFilterViolation("includeActive");
        if (request.IncludeCompleted == false && copy.EndedAt is not null)
            ThrowStoreFilterViolation("includeCompleted");

        return copy;
    }

    private static Dictionary<string, string> MergeTags(
        IReadOnlyDictionary<string, string>? defaults,
        IReadOnlyDictionary<string, string>? request)
    {
        var tags = defaults is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(defaults, StringComparer.Ordinal);
        if (request is null)
            return tags;

        foreach (var (key, value) in request)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new InvalidOperationException("session.query request tags cannot contain an empty key.");
            tags[key] = value;
        }

        return tags;
    }

    private static void ValidateRange(
        DateTimeOffset? from,
        DateTimeOffset? to,
        string fromName,
        string toName)
    {
        if (from.HasValue && to.HasValue && from.Value > to.Value)
        {
            throw new InvalidOperationException(
                $"session.query request {fromName} cannot be later than {toName}.");
        }
    }

    private static void ThrowStoreFilterViolation(string filterName)
        => throw new SessionContentOperationException(
            SessionErrorCodeNames.StoredContentInvalid,
            $"session.query store returned a session outside the query filter '{filterName}'.");

    private static bool IsTransient(Exception exception)
        => exception is SessionContentOperationException { IsTransient: true } or
            IOException or TimeoutException or SocketException;

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static FlowValue OptionalValue(string? value)
        => string.IsNullOrWhiteSpace(value) ? FlowValue.Null : FlowValue.From(value.Trim());
}
