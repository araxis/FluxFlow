using FluxFlow.Components.Sessions.Contracts;
using FluxFlow.Components.Sessions.Diagnostics;
using FluxFlow.Components.Sessions.Options;
using FluxFlow.Components.Sessions.Timing;
using FluxFlow.Engine.Components;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.Sessions.Nodes;

public sealed class SessionReplayNode : SourceFlowNode<SessionRecord>, IAsyncDisposable
{
    private readonly object _stateLock = new();
    private readonly SessionReplayOptions _options;
    private readonly ISessionStore _store;
    private readonly ISessionClock _clock;
    private readonly string _sessionId;
    private CancellationTokenSource? _replayCancellation;
    private Task? _replayTask;
    private bool _startRequested;
    private bool _disposed;

    internal SessionReplayNode(
        SessionReplayOptions options,
        ISessionStore store,
        ISessionClock clock)
        : base(new DataflowBlockOptions { BoundedCapacity = options.BoundedCapacity })
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _sessionId = Normalize(options.SessionId)
            ?? throw new ArgumentException("session.replay requires a session id.", nameof(options));
        if (options.BoundedCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "session.replay bounded capacity must be greater than zero.");
        }
    }

    public override async Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_stateLock)
        {
            if (_startRequested)
            {
                throw new InvalidOperationException("session.replay node has already started.");
            }

            _startRequested = true;
        }

        SessionMetadata? session;
        try
        {
            session = await _store.GetSessionAsync(
                _sessionId,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            TryReportReplayError(
                SessionsErrorCodes.StoreUnavailable,
                $"session.replay failed to load session: {exception.Message}",
                exception);
            lock (_stateLock)
            {
                _startRequested = false;
            }

            throw;
        }

        if (session is null)
        {
            var exception = new InvalidOperationException(
                $"session.replay session '{_sessionId}' was not found.");
            TryReportReplayError(
                SessionsErrorCodes.InvalidSession,
                exception.Message,
                exception);
            lock (_stateLock)
            {
                _startRequested = false;
            }

            throw exception;
        }

        lock (_stateLock)
        {
            _replayCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _replayTask = RunReplayAsync(_replayCancellation.Token);
        }

        TryEmitDiagnostic(
            SessionsDiagnosticNames.ReplayStarted,
            message: "session.replay started session.",
            attributes: CreateReplayAttributes(session));
    }

    public override void Complete()
    {
        CancellationTokenSource? cancellation;
        lock (_stateLock)
        {
            cancellation = _replayCancellation;
        }

        if (cancellation is null)
        {
            CompleteOutput();
            return;
        }

        cancellation.Cancel();
    }

    public override void Fault(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        _replayCancellation?.Cancel();
        base.Fault(exception);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Complete();
        if (_replayTask is not null)
        {
            await _replayTask.ConfigureAwait(false);
        }

        _replayCancellation?.Dispose();
    }

    private async Task RunReplayAsync(CancellationToken cancellationToken)
    {
        var emitted = 0L;
        SessionRecord? previous = null;
        try
        {
            await foreach (var record in _store.ReadMessagesAsync(
                               new SessionReadRequest
                               {
                                   SessionId = _sessionId,
                                   StartSequence = _options.StartSequence,
                                   MaxMessages = _options.MaxMessages
                               },
                               cancellationToken).WithCancellation(cancellationToken)
                               .ConfigureAwait(false))
            {
                await DelayForRecordAsync(previous, record, cancellationToken).ConfigureAwait(false);
                if (!await SendOutputAsync(CopyRecord(record), cancellationToken).ConfigureAwait(false))
                {
                    break;
                }

                emitted++;
                previous = record;
                TryEmitDiagnostic(
                    SessionsDiagnosticNames.ReplayEmitted,
                    message: "session.replay emitted message.",
                    attributes: CreateRecordAttributes(record, emitted));
            }

            TryEmitDiagnostic(
                SessionsDiagnosticNames.ReplayCompleted,
                message: "session.replay completed session.",
                attributes: CreateReplayAttributes(emitted));
            CompleteOutput();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            TryEmitDiagnostic(
                SessionsDiagnosticNames.ReplayCompleted,
                message: "session.replay stopped session.",
                attributes: CreateReplayAttributes(emitted));
            CompleteOutput();
        }
        catch (Exception exception)
        {
            TryReportReplayError(
                SessionsErrorCodes.ReplayFailed,
                $"session.replay failed: {exception.Message}",
                exception);
            base.Fault(exception);
        }
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

        await _clock.DelayAsync(delay, cancellationToken).ConfigureAwait(false);
    }

    private void TryReportReplayError(
        int code,
        string message,
        Exception? exception)
    {
        TryReportError(code, message, exception, $"sessionId={_sessionId}");
        TryEmitDiagnostic(
            SessionsDiagnosticNames.ReplayFailed,
            FlowDiagnosticLevel.Error,
            message,
            exception,
            CreateReplayAttributes());
    }

    private static SessionRecord CopyRecord(SessionRecord record)
        => record with
        {
            Attributes = record.Attributes is null
                ? []
                : new Dictionary<string, string>(record.Attributes, StringComparer.Ordinal)
        };

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
