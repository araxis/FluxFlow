using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Sessions.Contracts;
using FluxFlow.Components.Sessions.Diagnostics;
using FluxFlow.Components.Sessions.Options;
using FluxFlow.Data;
using FluxFlow.Nodes;

namespace FluxFlow.Components.Sessions.Nodes;

/// <summary>
/// Replays exact content records as normal operation results.
/// </summary>
public sealed class SessionReplayNode : FlowSource<SessionContentRecord>
{
    private readonly SessionReplayOptions _options;
    private readonly ISessionStore _store;
    private readonly TimeProvider _clock;
    private readonly string _sessionId;
    public SessionReplayNode(
        SessionReplayOptions options,
        ISessionStore store,
        TimeProvider? clock = null)
        : base(CreateSourceOptions(options))
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.BoundedCapacity, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.StartSequence ?? 1, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxMessages ?? 1, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(options.FixedIntervalMilliseconds);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.SpeedMultiplier, 0);

        _sessionId = string.IsNullOrWhiteSpace(options.SessionId)
            ? throw new ArgumentException("session.replay requires a session id.", nameof(options))
            : options.SessionId.Trim();
        _options = options;
        _store = store;
        _clock = clock ?? TimeProvider.System;
    }

    protected override async Task RunAsync(CancellationToken cancellationToken)
    {
        SessionMetadata session;
        try
        {
            var loaded = await _store.GetSessionAsync(_sessionId, cancellationToken)
                .ConfigureAwait(false);
            if (loaded is null)
            {
                var missing = new SessionContentOperationException(
                    SessionErrorCodeNames.SessionNotFound,
                    $"session.replay session '{_sessionId}' was not found.");
                await EmitFailureAsync(missing, SessionErrorCodeNames.SessionNotFound, cancellationToken)
                    .ConfigureAwait(false);
                return;
            }

            session = SessionContentNodeSupport.ValidateAndCopySession(
                loaded,
                "replay",
                _sessionId);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            await EmitFailureAsync(exception, SessionErrorCodeNames.StoreUnavailable, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        EmitEvent(SessionContentNodeSupport.CreateEvent(
            _clock.GetUtcNow(),
            SessionsDiagnosticNames.ReplayStarted,
            FlowEventLevel.Information,
            "session.replay started session.",
            SessionResultKinds.ReplayRecord,
            isError: false,
            "replay",
            _sessionId,
            count: checked((int)Math.Min(session.MessageCount, int.MaxValue))));

        var emitted = 0;
        SessionContentRecord? previous = null;
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
                throw new SessionContentOperationException(
                    SessionErrorCodeNames.StoreUnavailable,
                    "session.replay store returned a null message stream.");
            }

            await foreach (var record in records.WithCancellation(cancellationToken)
                               .ConfigureAwait(false))
            {
                SessionContentRecord contentRecord;
                try
                {
                    contentRecord = SessionContentNodeSupport.ValidateAndDecodeRecord(
                        record,
                        "replay",
                        _sessionId);
                }
                catch (Exception exception)
                {
                    await EmitFailureAsync(
                        exception,
                        SessionErrorCodeNames.ReplayFailed,
                        cancellationToken,
                        record?.Sequence).ConfigureAwait(false);
                    continue;
                }

                await DelayForRecordAsync(previous, contentRecord, cancellationToken)
                    .ConfigureAwait(false);
                var timestamp = _clock.GetUtcNow();
                var message = FlowMessage.Create(contentRecord);
                await EmitAsync(message, cancellationToken).ConfigureAwait(false);

                emitted++;
                previous = contentRecord;
                EmitEvent(SessionContentNodeSupport.CreateEvent(
                    timestamp,
                    SessionsDiagnosticNames.ReplayEmitted,
                    FlowEventLevel.Information,
                    "session.replay emitted content.",
                    SessionResultKinds.ReplayRecord,
                    isError: false,
                    "replay",
                    _sessionId,
                    message.CorrelationId,
                    sequence: contentRecord.Sequence,
                    count: emitted));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            await EmitFailureAsync(exception, SessionErrorCodeNames.ReplayFailed, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        EmitEvent(SessionContentNodeSupport.CreateEvent(
            _clock.GetUtcNow(),
            SessionsDiagnosticNames.ReplayCompleted,
            FlowEventLevel.Information,
            "session.replay completed session.",
            SessionResultKinds.ReplayRecord,
            isError: false,
            "replay",
            _sessionId,
            count: emitted));
    }

    private async Task EmitFailureAsync(
        Exception exception,
        string fallbackCode,
        CancellationToken cancellationToken,
        long? sequence = null)
    {
        var failure = SessionContentNodeSupport.Classify(
            exception,
            "replay",
            fallbackCode);
        var timestamp = _clock.GetUtcNow();
        var error = SessionContentNodeSupport.CreateError(
            failure.Code,
            failure.Message,
            "replay",
            _sessionId,
            exception,
            sequence);
        var message = FlowMessage.CreateError<SessionContentRecord>(error);
        await EmitAsync(message, cancellationToken).ConfigureAwait(false);
        EmitEvent(SessionContentNodeSupport.CreateEvent(
            timestamp,
            SessionsDiagnosticNames.ReplayFailed,
            FlowEventLevel.Warning,
            failure.Message,
            SessionResultKinds.ReplayFailed,
            isError: true,
            "replay",
            _sessionId,
            message.CorrelationId,
            errorCode: failure.Code,
            sequence: sequence));
    }

    private async Task DelayForRecordAsync(
        SessionContentRecord? previous,
        SessionContentRecord current,
        CancellationToken cancellationToken)
    {
        if (previous is null || _options.Mode == SessionReplayMode.Instant)
            return;

        var delay = _options.Mode switch
        {
            SessionReplayMode.FixedInterval =>
                TimeSpan.FromMilliseconds(_options.FixedIntervalMilliseconds),
            SessionReplayMode.RealTime => current.Timestamp - previous.Timestamp,
            SessionReplayMode.Multiplier => TimeSpan.FromTicks(
                (long)Math.Max(
                    0,
                    (current.Timestamp - previous.Timestamp).Ticks / _options.SpeedMultiplier)),
            _ => TimeSpan.Zero
        };
        if (delay > TimeSpan.Zero)
            await Task.Delay(delay, _clock, cancellationToken).ConfigureAwait(false);
    }

    private static FlowSourceOptions CreateSourceOptions(SessionReplayOptions? options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.BoundedCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "session.replay option 'boundedCapacity' must be greater than zero.");
        }

        return new FlowSourceOptions
        {
            OutputCapacity = options.BoundedCapacity
        };
    }
}
