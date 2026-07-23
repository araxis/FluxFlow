using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Sessions.Contracts;
using FluxFlow.Components.Sessions.Diagnostics;
using FluxFlow.Components.Sessions.Options;
using FluxFlow.Data;
using FluxFlow.Nodes;

namespace FluxFlow.Components.Sessions.Nodes;

/// <summary>
/// Queries host-owned session stores and emits one normal result per request.
/// </summary>
public sealed class SessionQueryNode : IFlowNode
{
    private readonly SessionQueryOptions _options;
    private readonly ISessionStore _store;
    private readonly TimeProvider _clock;
    private readonly SessionOperationPipeline<SessionQueryRequest, SessionQueryOutcome> _pipeline;

    public SessionQueryNode(
        SessionQueryOptions options,
        ISessionStore store,
        TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.BoundedCapacity, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.Limit, 1);
        if (!options.IncludeActive && !options.IncludeCompleted)
        {
            throw new ArgumentException(
                "session.query must include active sessions, completed sessions, or both.",
                nameof(options));
        }

        _options = options;
        _store = store;
        _clock = clock ?? TimeProvider.System;
        _pipeline = new(options.BoundedCapacity, ProcessAsync);
        _pipeline.PublishEvent(SessionContentNodeSupport.CreateEvent(
            _clock.GetUtcNow(),
            SessionsDiagnosticNames.QueryStarted,
            FlowEventLevel.Information,
            "session.query started.",
            SessionResultKinds.QueryCompleted,
            isError: false,
            "query",
            sessionId: null));
    }

    public ITargetBlock<FlowMessage<SessionQueryRequest>> Input => _pipeline.Input;

    public ISourceBlock<FlowMessage<FlowResult<SessionQueryOutcome>>> Output => _pipeline.Output;

    public ISourceBlock<FlowEvent> Events => _pipeline.Events;

    public Task Completion => _pipeline.Completion;

    public void Complete() => _pipeline.Complete();

    public void Fault(Exception exception) => _pipeline.Fault(exception);

    public ValueTask DisposeAsync() => _pipeline.DisposeAsync();

    private async Task<FlowMessage<FlowResult<SessionQueryOutcome>>> ProcessAsync(
        FlowMessage<SessionQueryRequest> message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var input = message.Payload;

        try
        {
            if (input is null)
            {
                throw new SessionContentOperationException(
                    SessionErrorCodeNames.InvalidRequest,
                    "session.query requires an input request.");
            }

            var request = SessionContentNodeSupport.NormalizeRequest(
                "query",
                () => SessionContentNodeSupport.NormalizeQuery(input, _options));
            var queried = await _store.QuerySessionsAsync(request, cancellationToken)
                .ConfigureAwait(false);
            var sessions = SessionContentNodeSupport.ValidateQuerySessions(request, queried);
            var outcome = new SessionQueryOutcome
            {
                Count = sessions.Count,
                Sessions = _options.EmitSessionsInResult ? sessions : Array.Empty<SessionMetadata>()
            };
            var timestamp = _clock.GetUtcNow();
            _pipeline.PublishEvent(SessionContentNodeSupport.CreateEvent(
                timestamp,
                SessionsDiagnosticNames.QueryCompleted,
                FlowEventLevel.Information,
                "session.query completed.",
                SessionResultKinds.QueryCompleted,
                isError: false,
                "query",
                sessionId: null,
                message.CorrelationId,
                count: sessions.Count));
            return message.With(FlowResult<SessionQueryOutcome>.Success(
                SessionResultKinds.QueryCompleted,
                outcome,
                timestamp));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            var failure = SessionContentNodeSupport.Classify(
                exception,
                "query",
                SessionErrorCodeNames.QueryFailed);
            var timestamp = _clock.GetUtcNow();
            var error = SessionContentNodeSupport.CreateError(
                failure.Code,
                failure.Message,
                "query",
                sessionId: null,
                exception);
            _pipeline.PublishEvent(SessionContentNodeSupport.CreateEvent(
                timestamp,
                SessionsDiagnosticNames.QueryFailed,
                FlowEventLevel.Warning,
                failure.Message,
                SessionResultKinds.QueryFailed,
                isError: true,
                "query",
                sessionId: null,
                message.CorrelationId,
                errorCode: failure.Code));
            return message.With(FlowResult<SessionQueryOutcome>.Failure(
                SessionResultKinds.QueryFailed,
                error,
                timestamp));
        }
    }
}
