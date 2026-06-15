using FluxFlow.Components.Sessions.Contracts;
using FluxFlow.Components.Sessions.Diagnostics;
using FluxFlow.Components.Sessions.Options;
using FluxFlow.Engine.Components;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.Sessions.Nodes;

public sealed class SessionQueryNode : FlowNodeBase, IAsyncDisposable
{
    private readonly object _stateLock = new();
    private readonly SessionQueryOptions _options;
    private readonly ISessionStore _store;
    private readonly SessionsComponentOptions _componentOptions;
    private readonly ActionBlock<SessionQueryRequest> _input;
    private readonly BufferBlock<SessionQueryResult> _output;
    private readonly BufferBlock<SessionMetadata> _sessions;
    private readonly CancellationTokenSource _processingCancellation = new();
    private bool _startRequested;
    private bool _disposed;

    internal SessionQueryNode(
        SessionQueryOptions options,
        ISessionStore store,
        SessionsComponentOptions componentOptions)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _componentOptions = componentOptions ?? throw new ArgumentNullException(nameof(componentOptions));
        if (options.BoundedCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "session.query bounded capacity must be greater than zero.");
        }

        var blockOptions = new DataflowBlockOptions { BoundedCapacity = options.BoundedCapacity };
        _input = new ActionBlock<SessionQueryRequest>(
            QueryAsync,
            new ExecutionDataflowBlockOptions
            {
                BoundedCapacity = options.BoundedCapacity,
                EnsureOrdered = true,
                MaxDegreeOfParallelism = 1
            });
        _output = new BufferBlock<SessionQueryResult>(blockOptions);
        _sessions = new BufferBlock<SessionMetadata>(blockOptions);
        _input.Completion.ContinueWith(
            CompleteOutputs,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        CompleteWhen(Task.WhenAll(_output.Completion, _sessions.Completion));
    }

    public ITargetBlock<SessionQueryRequest> Input => _input;

    public ISourceBlock<SessionQueryResult> Output => _output;

    public ISourceBlock<SessionMetadata> Sessions => _sessions;

    public override Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_stateLock)
        {
            if (_startRequested)
            {
                throw new InvalidOperationException("session.query node has already started.");
            }

            _startRequested = true;
        }

        TryEmitDiagnostic(
            SessionsDiagnosticNames.QueryStarted,
            message: "session.query started.",
            attributes: CreateQueryAttributes());
        return Task.CompletedTask;
    }

    public override void Complete()
        => _input.Complete();

    public override void Fault(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        try
        {
            _processingCancellation.Cancel();
            FaultNode(exception);
        }
        finally
        {
            ((IDataflowBlock)_input).Fault(exception);
            ((IDataflowBlock)_output).Fault(exception);
            ((IDataflowBlock)_sessions).Fault(exception);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Complete();
        await Completion.ConfigureAwait(false);
        _processingCancellation.Dispose();
    }

    private async Task QueryAsync(SessionQueryRequest input)
    {
        ArgumentNullException.ThrowIfNull(input);

        lock (_stateLock)
        {
            if (!_startRequested)
            {
                ReportQueryError(
                    SessionsErrorCodes.NotStarted,
                    "session.query has not started.",
                    input,
                    exception: null);
                return;
            }
        }

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
                input,
                exception);
            return;
        }

        try
        {
            var sessions = await _store.QuerySessionsAsync(
                request,
                _processingCancellation.Token).ConfigureAwait(false);
            var copiedSessions = sessions
                .Select(ValidateAndCopySession)
                .Take(request.Limit!.Value)
                .ToArray();

            await _output.SendAsync(
                CreateResult(request, copiedSessions),
                _processingCancellation.Token).ConfigureAwait(false);

            if (_options.EmitSessionOutputs)
            {
                foreach (var session in copiedSessions)
                {
                    await _sessions.SendAsync(
                        session,
                        _processingCancellation.Token).ConfigureAwait(false);
                }
            }

            TryEmitDiagnostic(
                SessionsDiagnosticNames.QueryCompleted,
                message: "session.query completed.",
                attributes: CreateQueryAttributes(request, copiedSessions.Length));
        }
        catch (OperationCanceledException) when (_processingCancellation.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            ReportQueryError(
                SessionsErrorCodes.QueryFailed,
                $"session.query failed: {exception.Message}",
                request,
                exception);
        }
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
            Timestamp = _componentOptions.Clock.GetUtcNow(),
            Operation = "query",
            Succeeded = true,
            Count = sessions.Count,
            Sessions = _options.EmitSessionsInResult ? sessions.ToArray() : [],
            CorrelationId = request.CorrelationId
        };

    private static SessionMetadata ValidateAndCopySession(SessionMetadata session)
    {
        if (string.IsNullOrWhiteSpace(session.SessionId))
        {
            throw new InvalidOperationException(
                "session.query store returned a session without a session id.");
        }

        return session with
        {
            Tags = CopyDictionary(session.Tags)
        };
    }

    private void ReportQueryError(
        int code,
        string message,
        SessionQueryRequest request,
        Exception? exception)
    {
        var correlationId = Normalize(request.CorrelationId);
        TryReportError(
            code,
            message,
            exception,
            CreateQueryContext(request, correlationId));
        TryEmitDiagnostic(
            SessionsDiagnosticNames.QueryFailed,
            FlowDiagnosticLevel.Error,
            message,
            exception,
            CreateQueryAttributes(request));
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

    private void CompleteOutputs(Task completion)
    {
        if (completion.IsFaulted && completion.Exception is { } exception)
        {
            ((IDataflowBlock)_output).Fault(exception);
            ((IDataflowBlock)_sessions).Fault(exception);
            return;
        }

        _output.Complete();
        _sessions.Complete();
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
