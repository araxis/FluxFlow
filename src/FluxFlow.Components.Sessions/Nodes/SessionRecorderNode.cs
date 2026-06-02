using FluxFlow.Components.Sessions.Contracts;
using FluxFlow.Components.Sessions.Diagnostics;
using FluxFlow.Components.Sessions.Options;
using FluxFlow.Components.Sessions.Timing;
using FluxFlow.Engine.Components;
using FluxFlow.Engine.Runtime;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.Sessions.Nodes;

public sealed class SessionRecorderNode : FlowNodeBase, IAsyncDisposable
{
    private readonly object _stateLock = new();
    private readonly SessionRecorderOptions _options;
    private readonly ISessionStore _store;
    private readonly ISessionClock _clock;
    private readonly ActionBlock<SessionRecordInput> _input;
    private readonly BufferBlock<SessionRecord> _output;
    private readonly CancellationTokenSource _processingCancellation = new();
    private SessionMetadata? _session;
    private long _sequence;
    private bool _startRequested;
    private bool _disposed;

    private SessionRecorderNode(
        SessionRecorderOptions options,
        ISessionStore store,
        ISessionClock clock)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        if (options.BoundedCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "session.recorder bounded capacity must be greater than zero.");
        }

        _input = new ActionBlock<SessionRecordInput>(
            RecordAsync,
            new ExecutionDataflowBlockOptions
            {
                BoundedCapacity = options.BoundedCapacity,
                EnsureOrdered = true,
                MaxDegreeOfParallelism = 1
            });
        _output = new BufferBlock<SessionRecord>(
            new DataflowBlockOptions { BoundedCapacity = options.BoundedCapacity });
        _ = _input.Completion.ContinueWith(
            completion => CompleteRecordingAsync(completion),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default).Unwrap();
    }

    public ITargetBlock<SessionRecordInput> Input => _input;

    public ISourceBlock<SessionRecord> Output => _output;

    public static RuntimeNode Create(
        RuntimeNodeFactoryContext context,
        SessionsComponentOptions componentOptions)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(componentOptions);

        var options = SessionsOptionsReader.ReadRecorderOptions(context.Definition);
        var store = componentOptions.StoreFactory.Create(new SessionStoreContext
        {
            Address = context.Address,
            NodeType = SessionsComponentTypes.Recorder,
            StoreName = Normalize(options.Store),
            SessionId = Normalize(options.SessionId)
        }) ?? throw new InvalidOperationException("session.recorder store factory returned null.");
        var node = new SessionRecorderNode(options, store, componentOptions.Clock);

        return context.CreateNode(node)
            .Input(SessionsComponentPorts.Input, node.Input)
            .Output(SessionsComponentPorts.Output, node.Output)
            .Output(SessionsComponentPorts.Errors, node.Errors)
            .Build();
    }

    public override async Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_stateLock)
        {
            if (_startRequested)
            {
                throw new InvalidOperationException("session.recorder node has already started.");
            }

            _startRequested = true;
        }

        SessionMetadata session;
        try
        {
            session = await _store.StartSessionAsync(
                new SessionStartRequest
                {
                    SessionId = Normalize(_options.SessionId),
                    Name = Normalize(_options.Name),
                    StartedAt = _clock.UtcNow,
                    Notes = Normalize(_options.Notes),
                    Tags = CopyDictionary(_options.Tags)
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            TryReportError(
                SessionsErrorCodes.StoreUnavailable,
                $"session.recorder failed to start session: {exception.Message}",
                exception);
            TryEmitDiagnostic(
                SessionsDiagnosticNames.RecorderFailed,
                FlowDiagnosticLevel.Error,
                "session.recorder failed to start session.",
                exception,
                CreateSessionAttributes());
            lock (_stateLock)
            {
                _startRequested = false;
            }

            throw;
        }

        if (string.IsNullOrWhiteSpace(session.SessionId))
        {
            throw new InvalidOperationException(
                "session.recorder store returned a session without a session id.");
        }

        lock (_stateLock)
        {
            _session = session;
            _sequence = Math.Max(0, session.MessageCount);
        }

        TryEmitDiagnostic(
            SessionsDiagnosticNames.RecorderStarted,
            message: "session.recorder started session.",
            attributes: CreateSessionAttributes(session));
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

    private async Task RecordAsync(SessionRecordInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        SessionMetadata? session;
        lock (_stateLock)
        {
            session = _session;
        }

        if (session is null)
        {
            ReportRecorderError(
                SessionsErrorCodes.InvalidSession,
                "session.recorder has not started.",
                input,
                exception: null);
            return;
        }

        var sequence = _sequence + 1;
        var timestamp = input.Timestamp ?? _clock.UtcNow;

        try
        {
            var record = await _store.AppendMessageAsync(
                new SessionAppendRequest
                {
                    Session = session,
                    Input = CopyInput(input, timestamp),
                    Sequence = sequence,
                    Timestamp = timestamp
                },
                _processingCancellation.Token).ConfigureAwait(false);

            ValidateRecord(record, session.SessionId, sequence);
            _sequence = record.Sequence;
            await _output.SendAsync(record, _processingCancellation.Token).ConfigureAwait(false);
            TryEmitDiagnostic(
                SessionsDiagnosticNames.RecorderRecorded,
                message: "session.recorder recorded message.",
                attributes: CreateRecordAttributes(record));
        }
        catch (OperationCanceledException) when (_processingCancellation.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            ReportRecorderError(
                SessionsErrorCodes.RecorderFailed,
                $"session.recorder failed to record message: {exception.Message}",
                input,
                exception);
        }
    }

    private async Task CompleteRecordingAsync(Task completion)
    {
        if (completion.IsFaulted && completion.Exception is { } completionException)
        {
            ((IDataflowBlock)_output).Fault(completionException.InnerException ?? completionException);
            FaultNode(completionException.InnerException ?? completionException);
            return;
        }

        SessionMetadata? session;
        lock (_stateLock)
        {
            session = _session;
        }

        if (session is not null)
        {
            try
            {
                var completed = await _store.CompleteSessionAsync(
                    new SessionCompleteRequest
                    {
                        Session = session,
                        EndedAt = _clock.UtcNow,
                        MessageCount = _sequence
                    },
                    CancellationToken.None).ConfigureAwait(false);
                TryEmitDiagnostic(
                    SessionsDiagnosticNames.RecorderCompleted,
                    message: "session.recorder completed session.",
                    attributes: CreateSessionAttributes(completed));
            }
            catch (Exception exception)
            {
                TryReportError(
                    SessionsErrorCodes.RecorderFailed,
                    $"session.recorder failed to complete session: {exception.Message}",
                    exception,
                    CreateSessionContext(session));
                TryEmitDiagnostic(
                    SessionsDiagnosticNames.RecorderFailed,
                    FlowDiagnosticLevel.Error,
                    "session.recorder failed to complete session.",
                    exception,
                    CreateSessionAttributes(session));
                ((IDataflowBlock)_output).Fault(exception);
                FaultNode(exception);
                return;
            }
        }

        _output.Complete();
        CompleteNode();
    }

    private void ReportRecorderError(
        int code,
        string message,
        SessionRecordInput input,
        Exception? exception)
    {
        TryReportError(code, message, exception, CreateInputContext(input));
        TryEmitDiagnostic(
            SessionsDiagnosticNames.RecorderFailed,
            FlowDiagnosticLevel.Error,
            message,
            exception,
            CreateInputAttributes(input));
    }

    private static void ValidateRecord(
        SessionRecord record,
        string expectedSessionId,
        long expectedSequence)
    {
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

    private static string CreateSessionContext(SessionMetadata session)
        => $"sessionId={session.SessionId}";

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
