using FluxFlow.Components.Sessions.Contracts;
using FluxFlow.Components.Sessions.Diagnostics;
using FluxFlow.Components.Sessions.Options;
using FluxFlow.Nodes;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.Sessions.Nodes;

/// <summary>
/// A standalone session query node. Post a <c>FlowMessage&lt;SessionQueryRequest&gt;</c>
/// to <c>Input</c>; the node merges the request with its defaults, queries a
/// host-provided <see cref="ISessionStore"/>, and broadcasts a
/// <c>FlowMessage&lt;SessionQueryResult&gt;</c> on <c>Output</c> carrying the same
/// correlation id. When <see cref="SessionQueryOptions.EmitSessionOutputs"/> is set it
/// also fans each matching <c>FlowMessage&lt;SessionMetadata&gt;</c> out to the extra
/// <c>Sessions</c> port. Invalid requests and store failures surface on <c>Errors</c>
/// (with the original correlation id) and the pump keeps processing later requests;
/// diagnostics go to <c>Events</c>. Works with nothing but
/// <c>new SessionQueryNode(options, store)</c> — no engine.
/// </summary>
public sealed class SessionQueryNode : FlowNode<SessionQueryRequest, SessionQueryResult>
{
    public const string QueryStarted = SessionsDiagnosticNames.QueryStarted;
    public const string QueryCompleted = SessionsDiagnosticNames.QueryCompleted;
    public const string QueryFailed = SessionsDiagnosticNames.QueryFailed;

    private readonly SessionQueryOptions _options;
    private readonly ISessionStore _store;
    private readonly TimeProvider _clock;
    private readonly BroadcastBlock<FlowMessage<SessionMetadata>> _sessions;

    public SessionQueryNode(
        SessionQueryOptions options,
        ISessionStore store,
        TimeProvider? clock = null)
        : base(new FlowNodeOptions
        {
            InputCapacity = (options ?? throw new ArgumentNullException(nameof(options))).BoundedCapacity
        })
    {
        if (options.BoundedCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "session.query bounded capacity must be greater than zero.");
        }

        if (options.Limit <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "session.query limit must be greater than zero.");
        }

        if (!options.IncludeActive && !options.IncludeCompleted)
        {
            throw new ArgumentException(
                "session.query must include active sessions, completed sessions, or both.",
                nameof(options));
        }

        _options = options;
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _clock = clock ?? TimeProvider.System;
        _sessions = AddOutput<FlowMessage<SessionMetadata>>();

        // One-time "started" note, mirroring the old StartAsync diagnostic.
        EmitEvent(new FlowEvent
        {
            Timestamp = _clock.GetUtcNow(),
            Name = QueryStarted,
            Level = FlowEventLevel.Information,
            Message = "session.query started.",
            Attributes = CreateQueryAttributes()
        });
    }

    /// <summary>Matching session metadata; broadcast, carries the correlation id.</summary>
    public ISourceBlock<FlowMessage<SessionMetadata>> Sessions => _sessions;

    protected override async Task ProcessAsync(FlowMessage<SessionQueryRequest> message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var input = message.Payload;

        SessionQueryRequest request;
        try
        {
            request = NormalizeRequest(input);
        }
        catch (Exception exception)
        {
            ReportQueryError(
                SessionsErrorCodes.InvalidQuery,
                $"session.query request is invalid: {exception.Message}",
                message,
                input,
                exception);
            return;
        }

        IReadOnlyList<SessionMetadata> copiedSessions;
        try
        {
            var sessions = await _store.QuerySessionsAsync(request, Stopping).ConfigureAwait(false);
            if (sessions is null)
            {
                throw new InvalidOperationException(
                    "session.query store returned a null session query result.");
            }

            copiedSessions = sessions
                .Select(session => ValidateAndCopySession(request, session))
                .ToArray();
            if (copiedSessions.Count > request.Limit!.Value)
            {
                throw new InvalidOperationException(
                    "session.query store returned more sessions than requested.");
            }
        }
        catch (OperationCanceledException) when (Stopping.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            ReportQueryError(
                SessionsErrorCodes.QueryFailed,
                $"session.query failed: {exception.Message}",
                message,
                request,
                exception);
            return;
        }

        // Carry the correlation id forward onto the result and any branched session.
        Emit(message.With(CreateResult(request, copiedSessions)));

        if (_options.EmitSessionOutputs)
        {
            foreach (var session in copiedSessions)
            {
                _sessions.Post(message.With(session));
            }
        }

        EmitEvent(new FlowEvent
        {
            Timestamp = _clock.GetUtcNow(),
            CorrelationId = message.CorrelationId,
            Name = QueryCompleted,
            Level = FlowEventLevel.Information,
            Message = "session.query completed.",
            Attributes = CreateQueryAttributes(request, copiedSessions.Count)
        });
    }

    private SessionQueryRequest NormalizeRequest(SessionQueryRequest input)
    {
        var limit = input.Limit ?? _options.Limit;
        if (limit <= 0)
        {
            throw new InvalidOperationException("session.query request limit must be greater than zero.");
        }

        ValidateRange(input.StartedFrom, input.StartedTo, "startedFrom", "startedTo");
        ValidateRange(input.EndedFrom, input.EndedTo, "endedFrom", "endedTo");

        var includeActive = input.IncludeActive ?? _options.IncludeActive;
        var includeCompleted = input.IncludeCompleted ?? _options.IncludeCompleted;
        if (!includeActive && !includeCompleted)
        {
            throw new InvalidOperationException(
                "session.query must include active sessions, completed sessions, or both.");
        }

        return input with
        {
            Name = Normalize(input.Name) ?? Normalize(_options.Name),
            NamePrefix = Normalize(input.NamePrefix) ?? Normalize(_options.NamePrefix),
            Tags = MergeTags(_options.Tags, input.Tags),
            IncludeActive = includeActive,
            IncludeCompleted = includeCompleted,
            Limit = limit,
            CorrelationId = Normalize(input.CorrelationId)
        };
    }

    private SessionQueryResult CreateResult(
        SessionQueryRequest request,
        IReadOnlyList<SessionMetadata> sessions)
        => new()
        {
            Timestamp = _clock.GetUtcNow(),
            Operation = "query",
            Succeeded = true,
            Count = sessions.Count,
            Sessions = _options.EmitSessionsInResult ? sessions.ToArray() : [],
            CorrelationId = request.CorrelationId
        };

    private static SessionMetadata ValidateAndCopySession(
        SessionQueryRequest request,
        SessionMetadata? session)
    {
        if (session is null)
        {
            throw new InvalidOperationException(
                "session.query store returned a null session.");
        }

        if (string.IsNullOrWhiteSpace(session.SessionId))
        {
            throw new InvalidOperationException(
                "session.query store returned a session without a session id.");
        }

        ValidateSessionMatchesRequest(request, session);
        return session with
        {
            Tags = CopyDictionary(session.Tags)
        };
    }

    private static void ValidateSessionMatchesRequest(
        SessionQueryRequest request,
        SessionMetadata session)
    {
        if (!string.IsNullOrWhiteSpace(request.Name) &&
            !StringComparer.Ordinal.Equals(session.Name, request.Name))
        {
            ThrowStoreFilterViolation("name");
        }

        if (!string.IsNullOrWhiteSpace(request.NamePrefix) &&
            session.Name?.StartsWith(request.NamePrefix, StringComparison.Ordinal) != true)
        {
            ThrowStoreFilterViolation("namePrefix");
        }

        foreach (var (key, value) in request.Tags)
        {
            if (!session.Tags.TryGetValue(key, out var actual) ||
                !StringComparer.Ordinal.Equals(actual, value))
            {
                ThrowStoreFilterViolation($"tag '{key}'");
            }
        }

        if (request.StartedFrom.HasValue &&
            session.StartedAt < request.StartedFrom.Value)
        {
            ThrowStoreFilterViolation("startedFrom");
        }

        if (request.StartedTo.HasValue &&
            session.StartedAt > request.StartedTo.Value)
        {
            ThrowStoreFilterViolation("startedTo");
        }

        if (request.EndedFrom.HasValue &&
            (session.EndedAt is null || session.EndedAt.Value < request.EndedFrom.Value))
        {
            ThrowStoreFilterViolation("endedFrom");
        }

        if (request.EndedTo.HasValue &&
            (session.EndedAt is null || session.EndedAt.Value > request.EndedTo.Value))
        {
            ThrowStoreFilterViolation("endedTo");
        }

        if (request.IncludeActive == false && session.EndedAt is null)
        {
            ThrowStoreFilterViolation("includeActive");
        }

        if (request.IncludeCompleted == false && session.EndedAt is not null)
        {
            ThrowStoreFilterViolation("includeCompleted");
        }
    }

    private static void ThrowStoreFilterViolation(string filterName)
        => throw new InvalidOperationException(
            $"session.query store returned a session outside the query filter '{filterName}'.");

    private void ReportQueryError(
        int code,
        string message,
        FlowMessage<SessionQueryRequest> source,
        SessionQueryRequest request,
        Exception? exception)
    {
        var correlationId = Normalize(request.CorrelationId);
        EmitError(new FlowError
        {
            Timestamp = _clock.GetUtcNow(),
            CorrelationId = source.CorrelationId,
            Code = code,
            Message = message,
            Context = CreateQueryContext(request, correlationId),
            Exception = exception
        });
        EmitEvent(new FlowEvent
        {
            Timestamp = _clock.GetUtcNow(),
            CorrelationId = source.CorrelationId,
            Name = QueryFailed,
            Level = FlowEventLevel.Error,
            Message = message,
            Attributes = CreateQueryAttributes(request)
        });
    }

    private Dictionary<string, object?> CreateQueryAttributes(
        SessionQueryRequest? request = null,
        int? count = null)
    {
        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["store"] = _options.Store,
            ["boundedCapacity"] = _options.BoundedCapacity
        };

        if (request is not null)
        {
            attributes["name"] = request.Name;
            attributes["namePrefix"] = request.NamePrefix;
            attributes["tagCount"] = request.Tags?.Count ?? 0;
            attributes["startedFrom"] = request.StartedFrom;
            attributes["startedTo"] = request.StartedTo;
            attributes["endedFrom"] = request.EndedFrom;
            attributes["endedTo"] = request.EndedTo;
            attributes["includeActive"] = request.IncludeActive;
            attributes["includeCompleted"] = request.IncludeCompleted;
            attributes["limit"] = request.Limit;
            attributes["correlationId"] = request.CorrelationId;
        }

        if (count.HasValue)
        {
            attributes["count"] = count.Value;
        }

        return attributes;
    }

    private static string CreateQueryContext(
        SessionQueryRequest request,
        string? correlationId)
    {
        var values = new List<string>();
        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            values.Add($"name={request.Name}");
        }

        if (!string.IsNullOrWhiteSpace(request.NamePrefix))
        {
            values.Add($"namePrefix={request.NamePrefix}");
        }

        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            values.Add($"correlationId={correlationId}");
        }

        return string.Join("; ", values);
    }

    private static Dictionary<string, string> MergeTags(
        Dictionary<string, string>? defaults,
        Dictionary<string, string>? request)
    {
        var tags = CopyDictionary(defaults);
        if (request is null)
        {
            return tags;
        }

        foreach (var (key, value) in request)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new InvalidOperationException(
                    "session.query request tags cannot contain an empty key.");
            }

            tags[key] = value;
        }

        return tags;
    }

    private static Dictionary<string, string> CopyDictionary(Dictionary<string, string>? source)
        => source is null
            ? []
            : new Dictionary<string, string>(source, StringComparer.Ordinal);

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

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
