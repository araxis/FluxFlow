using FluxFlow.Components.Sessions.Contracts;
using FluxFlow.Components.Sessions.Diagnostics;
using FluxFlow.Components.Sessions.Options;
using FluxFlow.Nodes;

namespace FluxFlow.Components.Sessions.Nodes;

/// <summary>
/// A standalone session recorder node. Post a <c>FlowMessage&lt;SessionRecordInput&gt;</c>
/// to <c>Input</c>; the node appends each message to a host-provided
/// <see cref="ISessionStore"/> and broadcasts the stored
/// <c>FlowMessage&lt;SessionRecord&gt;</c> on <c>Output</c> (carrying the same correlation
/// id). The session is opened lazily on the first message and closed (with the final
/// message count) when the node is disposed. Append failures surface on <c>Errors</c>
/// with the original correlation id and the pump keeps processing later messages;
/// diagnostics go to <c>Events</c>. Works with nothing but
/// <c>new SessionRecorderNode(options, store)</c> — no engine.
/// </summary>
public sealed class SessionRecorderNode : FlowNode<SessionRecordInput, SessionRecord>
{
    public const string RecorderStarted = SessionsDiagnosticNames.RecorderStarted;
    public const string RecorderRecorded = SessionsDiagnosticNames.RecorderRecorded;
    public const string RecorderCompleted = SessionsDiagnosticNames.RecorderCompleted;
    public const string RecorderFailed = SessionsDiagnosticNames.RecorderFailed;

    private readonly SessionRecorderOptions _options;
    private readonly ISessionStore _store;
    private readonly TimeProvider _clock;
    private readonly TaskCompletionSource _completed =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private SessionMetadata? _session;
    private long _sequence;

    public SessionRecorderNode(
        SessionRecorderOptions options,
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
                "session.recorder bounded capacity must be greater than zero.");
        }

        _options = options;
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>
    /// Completes once the session has been closed in the store during disposal (or
    /// immediately if no message was ever recorded). Faults if the store's
    /// <see cref="ISessionStore.CompleteSessionAsync"/> throws while closing.
    /// </summary>
    public Task SessionCompleted => _completed.Task;

    protected override async Task ProcessAsync(FlowMessage<SessionRecordInput> message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var input = message.Payload;

        SessionMetadata session;
        try
        {
            session = await EnsureSessionStartedAsync(message).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            ReportRecorderError(
                SessionsErrorCodes.StoreUnavailable,
                $"session.recorder failed to start session: {exception.Message}",
                message,
                exception);
            return;
        }

        var sequence = _sequence + 1;
        var timestamp = input.Timestamp ?? _clock.GetUtcNow();

        SessionRecord record;
        try
        {
            record = await _store.AppendMessageAsync(
                new SessionAppendRequest
                {
                    Session = session,
                    Input = CopyInput(input, timestamp),
                    Sequence = sequence,
                    Timestamp = timestamp
                },
                Stopping).ConfigureAwait(false);
            record = ValidateRecord(record, session.SessionId, sequence);
        }
        catch (OperationCanceledException) when (Stopping.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            ReportRecorderError(
                SessionsErrorCodes.RecorderFailed,
                $"session.recorder failed to record message: {exception.Message}",
                message,
                exception);
            return;
        }

        _sequence = record.Sequence;

        // Carry the correlation id forward onto the stored record.
        Emit(message.With(record));
        EmitEvent(new FlowEvent
        {
            Timestamp = _clock.GetUtcNow(),
            CorrelationId = message.CorrelationId,
            Name = RecorderRecorded,
            Level = FlowEventLevel.Information,
            Message = "session.recorder recorded message.",
            Attributes = CreateRecordAttributes(record)
        });
    }

    /// <summary>
    /// Closes the session (with the final message count) once the pump has drained and
    /// the node is disposed. The output/error/event ports are already completed by this
    /// point, so the completion diagnostic surfaces on <see cref="SessionCompleted"/>
    /// rather than the <c>Events</c> stream; await it to observe the close.
    /// </summary>
    protected override async ValueTask OnDisposeAsync()
    {
        var session = _session;
        if (session is null)
        {
            _completed.TrySetResult();
            return;
        }

        try
        {
            var completed = await _store.CompleteSessionAsync(
                new SessionCompleteRequest
                {
                    Session = session,
                    EndedAt = _clock.GetUtcNow(),
                    MessageCount = _sequence
                },
                CancellationToken.None).ConfigureAwait(false);
            if (completed is null)
            {
                throw new InvalidOperationException(
                    "session.recorder store returned a null completed session.");
            }

            _completed.TrySetResult();
        }
        catch (Exception exception)
        {
            _completed.TrySetException(exception);
        }
    }

    private async Task<SessionMetadata> EnsureSessionStartedAsync(FlowMessage<SessionRecordInput> message)
    {
        if (_session is { } existing)
        {
            return existing;
        }

        var session = await _store.StartSessionAsync(
            new SessionStartRequest
            {
                SessionId = Normalize(_options.SessionId),
                Name = Normalize(_options.Name),
                StartedAt = _clock.GetUtcNow(),
                Notes = Normalize(_options.Notes),
                Tags = CopyDictionary(_options.Tags)
            },
            Stopping).ConfigureAwait(false);

        if (session is null)
        {
            throw new InvalidOperationException(
                "session.recorder store returned a null session.");
        }

        if (string.IsNullOrWhiteSpace(session.SessionId))
        {
            throw new InvalidOperationException(
                "session.recorder store returned a session without a session id.");
        }

        _session = session;
        _sequence = Math.Max(0, session.MessageCount);

        EmitEvent(new FlowEvent
        {
            Timestamp = _clock.GetUtcNow(),
            CorrelationId = message.CorrelationId,
            Name = RecorderStarted,
            Level = FlowEventLevel.Information,
            Message = "session.recorder started session.",
            Attributes = CreateSessionAttributes(session)
        });
        return session;
    }

    private void ReportRecorderError(
        int code,
        string message,
        FlowMessage<SessionRecordInput> source,
        Exception? exception)
    {
        EmitError(new FlowError
        {
            Timestamp = _clock.GetUtcNow(),
            CorrelationId = source.CorrelationId,
            Code = code,
            Message = message,
            Context = CreateInputContext(source.Payload),
            Exception = exception
        });
        EmitEvent(new FlowEvent
        {
            Timestamp = _clock.GetUtcNow(),
            CorrelationId = source.CorrelationId,
            Name = RecorderFailed,
            Level = FlowEventLevel.Error,
            Message = message,
            Attributes = CreateInputAttributes(source.Payload)
        });
    }

    private static SessionRecord ValidateRecord(
        SessionRecord? record,
        string expectedSessionId,
        long expectedSequence)
    {
        if (record is null)
        {
            throw new InvalidOperationException(
                "session.recorder store returned a null record.");
        }

        if (string.IsNullOrWhiteSpace(record.SessionId))
        {
            throw new InvalidOperationException(
                "session.recorder store returned a record without a session id.");
        }

        if (!StringComparer.Ordinal.Equals(record.SessionId, expectedSessionId))
        {
            throw new InvalidOperationException(
                "session.recorder store returned a record for a different session.");
        }

        if (record.Sequence != expectedSequence)
        {
            throw new InvalidOperationException(
                "session.recorder store returned a record with an invalid sequence.");
        }

        return record;
    }

    private static SessionRecordInput CopyInput(SessionRecordInput input, DateTimeOffset timestamp)
        => input with
        {
            Timestamp = timestamp,
            Attributes = CopyDictionary(input.Attributes)
        };

    private static Dictionary<string, string> CopyDictionary(Dictionary<string, string>? source)
        => source is null
            ? []
            : new Dictionary<string, string>(source, StringComparer.Ordinal);

    private static Dictionary<string, object?> CreateSessionAttributes(SessionMetadata? session = null)
    {
        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (session is not null)
        {
            attributes["sessionId"] = session.SessionId;
            attributes["name"] = session.Name;
            attributes["messageCount"] = session.MessageCount;
            attributes["startedAt"] = session.StartedAt;
            attributes["endedAt"] = session.EndedAt;
        }

        return attributes;
    }

    private static Dictionary<string, object?> CreateRecordAttributes(SessionRecord record)
        => new(StringComparer.Ordinal)
        {
            ["sessionId"] = record.SessionId,
            ["sequence"] = record.Sequence,
            ["timestamp"] = record.Timestamp,
            ["type"] = record.Type,
            ["name"] = record.Name
        };

    private static Dictionary<string, object?> CreateInputAttributes(SessionRecordInput input)
        => new(StringComparer.Ordinal)
        {
            ["type"] = input.Type,
            ["name"] = input.Name,
            ["contentType"] = input.ContentType,
            ["hasPayload"] = input.Payload is not null,
            ["attributeCount"] = input.Attributes?.Count ?? 0
        };

    private static string CreateInputContext(SessionRecordInput input)
    {
        var values = new List<string>();
        if (!string.IsNullOrWhiteSpace(input.Type))
        {
            values.Add($"type={input.Type}");
        }

        if (!string.IsNullOrWhiteSpace(input.Name))
        {
            values.Add($"name={input.Name}");
        }

        return string.Join("; ", values);
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
