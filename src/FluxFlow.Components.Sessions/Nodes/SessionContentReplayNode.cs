using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Sessions.Contracts;
using FluxFlow.Components.Sessions.Diagnostics;
using FluxFlow.Components.Sessions.Options;
using FluxFlow.Data;
using FluxFlow.Nodes;

namespace FluxFlow.Components.Sessions.Nodes;

/// <summary>
/// Canonical session replay source that emits exact content records as normal
/// typed results. Store and record failures are output data rather than node faults.
/// </summary>
public sealed class SessionContentReplayNode : IFlowSource
{
    private readonly SessionReplayOptions _options;
    private readonly ISessionStore _store;
    private readonly TimeProvider _clock;
    private readonly string _sessionId;
    private readonly BroadcastBlock<FlowMessage<FlowResult<SessionContentRecord>>> _output;
    private readonly BroadcastBlock<FlowEvent> _events = new(static @event => @event);
    private readonly CancellationTokenSource _stopping = new();
    private readonly TaskCompletionSource _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _started;
    private int _disposed;

    public SessionContentReplayNode(
        SessionReplayOptions options,
        ISessionStore store,
        TimeProvider? clock = null)
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
        _output = new BroadcastBlock<FlowMessage<FlowResult<SessionContentRecord>>>(
            static message => message,
            new DataflowBlockOptions { BoundedCapacity = options.BoundedCapacity });
    }

    public ISourceBlock<FlowMessage<FlowResult<SessionContentRecord>>> Output => _output;

    public ISourceBlock<FlowEvent> Events => _events;

    public Task Completion => _completion.Task;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled(cancellationToken);
        if (Interlocked.Exchange(ref _started, 1) != 0)
            return Task.CompletedTask;

        _ = ProduceAsync();
        return Task.CompletedTask;
    }

    public void Complete() => _stopping.Cancel();

    public void Fault(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        _stopping.Cancel();
        FaultOutputs(exception);
        _completion.TrySetException(exception);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _stopping.Cancel();
        if (Volatile.Read(ref _started) == 0)
        {
            CompleteOutputs();
            _completion.TrySetResult();
        }

        try
        {
            await Completion.ConfigureAwait(false);
        }
        catch
        {
            // Completion remains the authoritative unexpected-fault surface.
        }
        finally
        {
            _stopping.Dispose();
        }
    }

    private async Task ProduceAsync()
    {
        try
        {
            await RunAsync(_stopping.Token).ConfigureAwait(false);
            CompleteOutputs();
            await Task.WhenAll(_output.Completion, _events.Completion).ConfigureAwait(false);
            _completion.TrySetResult();
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
            CompleteOutputs();
            await Task.WhenAll(_output.Completion, _events.Completion).ConfigureAwait(false);
            _completion.TrySetResult();
        }
        catch (Exception exception)
        {
            FaultOutputs(exception);
            _completion.TrySetException(exception);
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
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

        _events.Post(SessionContentNodeSupport.CreateEvent(
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
                    if (!await EmitFailureAsync(
                            exception,
                            SessionErrorCodeNames.ReplayFailed,
                            cancellationToken,
                            record?.Sequence)
                        .ConfigureAwait(false))
                        return;
                    continue;
                }

                await DelayForRecordAsync(previous, contentRecord, cancellationToken)
                    .ConfigureAwait(false);
                var timestamp = _clock.GetUtcNow();
                var message = FlowMessage.Create(FlowResult<SessionContentRecord>.Success(
                    SessionResultKinds.ReplayRecord,
                    contentRecord,
                    timestamp));
                if (!await _output.SendAsync(message, cancellationToken).ConfigureAwait(false))
                    return;

                emitted++;
                previous = contentRecord;
                _events.Post(SessionContentNodeSupport.CreateEvent(
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

        _events.Post(SessionContentNodeSupport.CreateEvent(
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

    private async Task<bool> EmitFailureAsync(
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
        var message = FlowMessage.Create(FlowResult<SessionContentRecord>.Failure(
            SessionResultKinds.ReplayFailed,
            error,
            timestamp));
        var accepted = await _output.SendAsync(message, cancellationToken).ConfigureAwait(false);
        if (accepted)
        {
            _events.Post(SessionContentNodeSupport.CreateEvent(
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

        return accepted;
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

    private void CompleteOutputs()
    {
        _output.Complete();
        _events.Complete();
    }

    private void FaultOutputs(Exception exception)
    {
        try
        {
            ((IDataflowBlock)_output).Fault(exception);
        }
        catch
        {
            // The output may already be terminal.
        }

        _events.Complete();
    }
}
