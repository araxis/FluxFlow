using FluxFlow.Components.Sessions.Contracts;
using FluxFlow.Components.Sessions.Diagnostics;
using FluxFlow.Components.Sessions.Options;
using FluxFlow.Nodes;

namespace FluxFlow.Components.Sessions.Nodes;

/// <summary>
/// A standalone session replay source. Once <see cref="FlowSource{T}.StartAsync"/> is
/// called it reads the configured session's records from a host-provided
/// <see cref="ISessionStore"/> and broadcasts each as a fresh
/// <c>FlowMessage&lt;SessionRecord&gt;</c> on <c>Output</c> (each minted with a new
/// correlation id). Inter-record pacing — fixed interval, real-time deltas, or a
/// speed multiplier — is timed against the injected <see cref="TimeProvider"/>, so a
/// <c>FakeTimeProvider</c> drives it deterministically. The loop stops when the
/// session is exhausted, when <c>Stopping</c> fires (<c>Complete</c>/dispose), or when
/// the output declines delivery. A missing session or store failure faults the source
/// after surfacing a <see cref="FlowError"/>; diagnostics go to <c>Events</c>. Works
/// with nothing but <c>new SessionReplayNode(options, store)</c> — no engine.
/// </summary>
public sealed class SessionReplayNode : FlowSource<SessionRecord>
{
    public const string ReplayStarted = SessionsDiagnosticNames.ReplayStarted;
    public const string ReplayEmitted = SessionsDiagnosticNames.ReplayEmitted;
    public const string ReplayCompleted = SessionsDiagnosticNames.ReplayCompleted;
    public const string ReplayFailed = SessionsDiagnosticNames.ReplayFailed;

    private readonly SessionReplayOptions _options;
    private readonly ISessionStore _store;
    private readonly TimeProvider _clock;
    private readonly string _sessionId;

    public SessionReplayNode(
        SessionReplayOptions options,
        ISessionStore store,
        TimeProvider? clock = null)
        : base(BuildSourceOptions(options))
    {
        _options = options;
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _clock = clock ?? TimeProvider.System;
        _sessionId = Normalize(options.SessionId)
            ?? throw new ArgumentException("session.replay requires a session id.", nameof(options));

        if (options.StartSequence is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "session.replay start sequence must be greater than zero when set.");
        }

        if (options.MaxMessages is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "session.replay max messages must be greater than zero when set.");
        }

        if (options.FixedIntervalMilliseconds < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "session.replay fixed interval must be zero or greater.");
        }

        if (options.SpeedMultiplier <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "session.replay speed multiplier must be greater than zero.");
        }
    }

    protected override async Task RunAsync(CancellationToken cancellationToken)
    {
        SessionMetadata? session;
        try
        {
            session = await _store.GetSessionAsync(_sessionId, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            ReportReplayError(
                SessionsErrorCodes.StoreUnavailable,
                $"session.replay failed to load session: {exception.Message}",
                exception);
            throw;
        }

        if (session is null)
        {
            var missing = new InvalidOperationException(
                $"session.replay session '{_sessionId}' was not found.");
            ReportReplayError(SessionsErrorCodes.InvalidSession, missing.Message, missing);
            throw missing;
        }

        try
        {
            ValidateSession(session);
        }
        catch (Exception exception)
        {
            ReportReplayError(
                SessionsErrorCodes.ReplayFailed,
                $"session.replay failed: {exception.Message}",
                exception);
            throw;
        }

        EmitEvent(new FlowEvent
        {
            Timestamp = _clock.GetUtcNow(),
            Name = ReplayStarted,
            Level = FlowEventLevel.Information,
            Message = "session.replay started session.",
            Attributes = CreateReplayAttributes(session)
        });

        var emitted = 0L;
        SessionRecord? previous = null;
        try
        {
            var records = _store.ReadMessagesAsync(
                new SessionReadRequest
                {
                    SessionId = _sessionId,
                    StartSequence = _options.StartSequence,
                    MaxMessages = _options.MaxMessages
                },
                cancellationToken);
            if (records is null)
            {
                throw new InvalidOperationException(
                    "session.replay store returned a null message stream.");
            }

            await foreach (var record in records.WithCancellation(cancellationToken)
                               .ConfigureAwait(false))
            {
                var copiedRecord = ValidateAndCopyRecord(record);
                await DelayForRecordAsync(previous, copiedRecord, cancellationToken).ConfigureAwait(false);
                if (!await EmitAsync(FlowMessage.Create(copiedRecord), cancellationToken)
                        .ConfigureAwait(false))
                {
                    break;
                }

                emitted++;
                previous = copiedRecord;
                EmitEvent(new FlowEvent
                {
                    Timestamp = _clock.GetUtcNow(),
                    Name = ReplayEmitted,
                    Level = FlowEventLevel.Information,
                    Message = "session.replay emitted message.",
                    Attributes = CreateRecordAttributes(copiedRecord, emitted)
                });
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Cooperative stop (Complete/dispose); FlowSource treats it as normal completion.
            throw;
        }
        catch (Exception exception)
        {
            // A store failure that occurs mid-enumeration must still surface the
            // ReplayFailed FlowError + diagnostic before FlowSource faults Completion
            // (which completes/flushes the buffered Errors/Events ports).
            ReportReplayError(
                SessionsErrorCodes.ReplayFailed,
                $"session.replay failed: {exception.Message}",
                exception);
            throw;
        }

        EmitEvent(new FlowEvent
        {
            Timestamp = _clock.GetUtcNow(),
            Name = ReplayCompleted,
            Level = FlowEventLevel.Information,
            Message = "session.replay completed session.",
            Attributes = CreateReplayAttributes(emitted)
        });
    }

    private async Task DelayForRecordAsync(
        SessionRecord? previous,
        SessionRecord current,
        CancellationToken cancellationToken)
    {
        if (previous is null || _options.Mode == SessionReplayMode.Instant)
        {
            return;
        }

        var delay = _options.Mode switch
        {
            SessionReplayMode.FixedInterval => TimeSpan.FromMilliseconds(_options.FixedIntervalMilliseconds),
            SessionReplayMode.RealTime => current.Timestamp - previous.Timestamp,
            SessionReplayMode.Multiplier => TimeSpan.FromTicks(
                (long)Math.Max(0, (current.Timestamp - previous.Timestamp).Ticks / _options.SpeedMultiplier)),
            _ => TimeSpan.Zero
        };

        if (delay <= TimeSpan.Zero)
        {
            return;
        }

        await Task.Delay(delay, _clock, cancellationToken).ConfigureAwait(false);
    }

    private static FlowSourceOptions BuildSourceOptions(SessionReplayOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.BoundedCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "session.replay bounded capacity must be greater than zero.");
        }

        return new FlowSourceOptions { OutputCapacity = options.BoundedCapacity };
    }

    private void ReportReplayError(int code, string message, Exception? exception)
    {
        EmitError(new FlowError
        {
            Timestamp = _clock.GetUtcNow(),
            Code = code,
            Message = message,
            Context = $"sessionId={_sessionId}",
            Exception = exception
        });
        EmitEvent(new FlowEvent
        {
            Timestamp = _clock.GetUtcNow(),
            Name = ReplayFailed,
            Level = FlowEventLevel.Error,
            Message = message,
            Attributes = CreateReplayAttributes()
        });
    }

    private void ValidateSession(SessionMetadata session)
    {
        if (string.IsNullOrWhiteSpace(session.SessionId))
        {
            throw new InvalidOperationException(
                "session.replay store returned a session without a session id.");
        }

        if (!StringComparer.Ordinal.Equals(session.SessionId, _sessionId))
        {
            throw new InvalidOperationException(
                "session.replay store returned a different session.");
        }
    }

    private SessionRecord ValidateAndCopyRecord(SessionRecord? record)
    {
        if (record is null)
        {
            throw new InvalidOperationException(
                "session.replay store returned a null record.");
        }

        if (string.IsNullOrWhiteSpace(record.SessionId))
        {
            throw new InvalidOperationException(
                "session.replay store returned a record without a session id.");
        }

        if (!StringComparer.Ordinal.Equals(record.SessionId, _sessionId))
        {
            throw new InvalidOperationException(
                "session.replay store returned a record for a different session.");
        }

        return record with
        {
            Attributes = record.Attributes is null
                ? []
                : new Dictionary<string, string>(record.Attributes, StringComparer.Ordinal)
        };
    }

    private Dictionary<string, object?> CreateReplayAttributes(SessionMetadata? session = null)
    {
        var attributes = CreateReplayAttributes(emitted: null);
        if (session is not null)
        {
            attributes["name"] = session.Name;
            attributes["messageCount"] = session.MessageCount;
            attributes["startedAt"] = session.StartedAt;
            attributes["endedAt"] = session.EndedAt;
        }

        return attributes;
    }

    private Dictionary<string, object?> CreateReplayAttributes(long? emitted)
    {
        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["sessionId"] = _sessionId,
            ["mode"] = _options.Mode.ToString(),
            ["boundedCapacity"] = _options.BoundedCapacity
        };

        if (_options.StartSequence.HasValue)
        {
            attributes["startSequence"] = _options.StartSequence.Value;
        }

        if (_options.MaxMessages.HasValue)
        {
            attributes["maxMessages"] = _options.MaxMessages.Value;
        }

        if (emitted.HasValue)
        {
            attributes["emitted"] = emitted.Value;
        }

        return attributes;
    }

    private static Dictionary<string, object?> CreateRecordAttributes(
        SessionRecord record,
        long emitted)
        => new(StringComparer.Ordinal)
        {
            ["sessionId"] = record.SessionId,
            ["sequence"] = record.Sequence,
            ["timestamp"] = record.Timestamp,
            ["type"] = record.Type,
            ["name"] = record.Name,
            ["emitted"] = emitted
        };

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
