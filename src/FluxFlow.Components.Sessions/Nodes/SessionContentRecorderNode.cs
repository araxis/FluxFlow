using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Sessions.Contracts;
using FluxFlow.Components.Sessions.Diagnostics;
using FluxFlow.Components.Sessions.Options;
using FluxFlow.Data;
using FluxFlow.Nodes;

namespace FluxFlow.Components.Sessions.Nodes;

/// <summary>
/// Canonical session recorder for exact <see cref="FlowContent"/> values and
/// normal typed operation results.
/// </summary>
public sealed class SessionContentRecorderNode : IFlowNode
{
    private readonly SessionRecorderOptions _options;
    private readonly ISessionStore _store;
    private readonly TimeProvider _clock;
    private readonly SessionOperationPipeline<SessionContentRecordInput, SessionContentRecord> _pipeline;
    private readonly TaskCompletionSource _sessionCompleted =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private SessionMetadata? _session;
    private long _sequence;

    public SessionContentRecorderNode(
        SessionRecorderOptions options,
        ISessionStore store,
        TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.BoundedCapacity, 1);

        _options = options;
        _store = store;
        _clock = clock ?? TimeProvider.System;
        _pipeline = new(
            options.BoundedCapacity,
            ProcessAsync,
            CompleteSessionAsync);
    }

    public ITargetBlock<FlowMessage<SessionContentRecordInput>> Input => _pipeline.Input;

    public ISourceBlock<FlowMessage<FlowResult<SessionContentRecord>>> Output => _pipeline.Output;

    public ISourceBlock<FlowEvent> Events => _pipeline.Events;

    public Task Completion => _pipeline.Completion;

    /// <summary>
    /// Completes after the lazily-created session has been closed in the store.
    /// A store close failure faults this task but remains outside normal node completion.
    /// </summary>
    public Task SessionCompleted => _sessionCompleted.Task;

    public void Complete() => _pipeline.Complete();

    public void Fault(Exception exception) => _pipeline.Fault(exception);

    public ValueTask DisposeAsync() => _pipeline.DisposeAsync();

    private async Task<FlowMessage<FlowResult<SessionContentRecord>>> ProcessAsync(
        FlowMessage<SessionContentRecordInput> message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var input = message.Payload;
        string? sessionId = _session?.SessionId ?? _options.SessionId;
        long? sequence = null;

        try
        {
            if (input is null)
            {
                throw new SessionContentOperationException(
                    SessionErrorCodeNames.InvalidRequest,
                    "session.recorder requires an input request.");
            }

            var recordInput = SessionContentNodeSupport.NormalizeRequest(
                "recorder",
                () => SessionContentNodeSupport.CreateRecordInput(input));
            var session = await EnsureSessionStartedAsync(message.CorrelationId, cancellationToken)
                .ConfigureAwait(false);
            sessionId = session.SessionId;
            sequence = checked(_sequence + 1);
            var timestamp = input.Timestamp ?? _clock.GetUtcNow();
            var stored = await _store.AppendMessageAsync(
                    new SessionAppendRequest
                    {
                        Session = session,
                        Input = recordInput,
                        Sequence = sequence.Value,
                        Timestamp = timestamp
                    },
                    cancellationToken)
                .ConfigureAwait(false);
            var contentRecord = SessionContentNodeSupport.ValidateAndDecodeRecord(
                stored,
                "recorder",
                session.SessionId,
                sequence);
            _sequence = sequence.Value;

            var resultTimestamp = _clock.GetUtcNow();
            _pipeline.PublishEvent(SessionContentNodeSupport.CreateEvent(
                resultTimestamp,
                SessionsDiagnosticNames.RecorderRecorded,
                FlowEventLevel.Information,
                "session.recorder stored content.",
                SessionResultKinds.RecordStored,
                isError: false,
                "recorder",
                session.SessionId,
                message.CorrelationId,
                sequence: sequence));
            return message.With(FlowResult<SessionContentRecord>.Success(
                SessionResultKinds.RecordStored,
                contentRecord,
                resultTimestamp));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            var failure = SessionContentNodeSupport.Classify(
                exception,
                "recorder",
                SessionErrorCodeNames.RecordFailed);
            return Failure(
                message,
                failure.Code,
                failure.Message,
                sessionId,
                sequence,
                exception);
        }
    }

    private async Task<SessionMetadata> EnsureSessionStartedAsync(
        CorrelationId correlationId,
        CancellationToken cancellationToken)
    {
        if (_session is not null)
            return _session;

        var started = await _store.StartSessionAsync(
                new SessionStartRequest
                {
                    SessionId = _options.SessionId,
                    Name = _options.Name,
                    StartedAt = _clock.GetUtcNow(),
                    Notes = _options.Notes,
                    Tags = new Dictionary<string, string>(_options.Tags, StringComparer.Ordinal)
                },
                cancellationToken)
            .ConfigureAwait(false);
        _session = SessionContentNodeSupport.ValidateAndCopySession(
            started,
            "recorder",
            _options.SessionId);
        _sequence = Math.Max(0, _session.MessageCount);
        _pipeline.PublishEvent(SessionContentNodeSupport.CreateEvent(
            _clock.GetUtcNow(),
            SessionsDiagnosticNames.RecorderStarted,
            FlowEventLevel.Information,
            "session.recorder started session.",
            SessionResultKinds.RecordStored,
            isError: false,
            "recorder",
            _session.SessionId,
            correlationId));
        return _session;
    }

    private FlowMessage<FlowResult<SessionContentRecord>> Failure(
        FlowMessage<SessionContentRecordInput> message,
        string code,
        string text,
        string? sessionId,
        long? sequence,
        Exception exception)
    {
        var timestamp = _clock.GetUtcNow();
        var error = SessionContentNodeSupport.CreateError(
            code,
            text,
            "recorder",
            sessionId,
            exception,
            sequence);
        _pipeline.PublishEvent(SessionContentNodeSupport.CreateEvent(
            timestamp,
            SessionsDiagnosticNames.RecorderFailed,
            FlowEventLevel.Warning,
            text,
            SessionResultKinds.RecordFailed,
            isError: true,
            "recorder",
            sessionId,
            message.CorrelationId,
            errorCode: code,
            sequence: sequence));
        return message.With(FlowResult<SessionContentRecord>.Failure(
            SessionResultKinds.RecordFailed,
            error,
            timestamp));
    }

    private async Task CompleteSessionAsync()
    {
        if (_session is null)
        {
            _sessionCompleted.TrySetResult();
            return;
        }

        try
        {
            var completed = await _store.CompleteSessionAsync(
                    new SessionCompleteRequest
                    {
                        Session = _session,
                        EndedAt = _clock.GetUtcNow(),
                        MessageCount = _sequence
                    },
                    CancellationToken.None)
                .ConfigureAwait(false);
            _session = SessionContentNodeSupport.ValidateAndCopySession(
                completed,
                "recorder",
                _session.SessionId);
            _pipeline.PublishEvent(SessionContentNodeSupport.CreateEvent(
                _clock.GetUtcNow(),
                SessionsDiagnosticNames.RecorderCompleted,
                FlowEventLevel.Information,
                "session.recorder completed session.",
                SessionResultKinds.RecordStored,
                isError: false,
                "recorder",
                _session.SessionId,
                count: checked((int)Math.Min(_sequence, int.MaxValue))));
            _sessionCompleted.TrySetResult();
        }
        catch (Exception exception)
        {
            var text = $"session.recorder failed to complete session: {exception.Message}";
            _pipeline.PublishEvent(SessionContentNodeSupport.CreateEvent(
                _clock.GetUtcNow(),
                SessionsDiagnosticNames.RecorderFailed,
                FlowEventLevel.Warning,
                text,
                SessionResultKinds.RecordFailed,
                isError: true,
                "recorder",
                _session.SessionId,
                errorCode: SessionErrorCodeNames.CompleteFailed,
                count: checked((int)Math.Min(_sequence, int.MaxValue))));
            _sessionCompleted.TrySetException(new SessionContentOperationException(
                SessionErrorCodeNames.CompleteFailed,
                text,
                innerException: exception));
        }
    }
}
