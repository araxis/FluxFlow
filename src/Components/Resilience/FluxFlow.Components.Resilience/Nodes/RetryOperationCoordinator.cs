using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Resilience.Contracts;
using FluxFlow.Components.Resilience.Diagnostics;
using FluxFlow.Components.Resilience.Options;
using FluxFlow.Coordination;
using FluxFlow.Data;
using FluxFlow.Nodes;
using FluxFlow.Resilience;

using DataFlowError = FluxFlow.Data.FlowError;

namespace FluxFlow.Components.Resilience.Nodes;

internal sealed record RetryEmission(
    FlowMessage Input,
    RetrySignal<FlowValue>? Signal,
    DataFlowError? Error,
    IReadOnlyDictionary<string, string> Headers,
    MessageId CausationId);

internal sealed class RetryOperationCoordinator : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly FlowRetryOptions _options;
    private readonly TimeProvider _clock;
    private readonly IRetryJitterSource _jitterSource;
    private readonly Func<RetryEmission, CancellationToken, ValueTask<MessageId>> _emit;
    private readonly PendingExchangeCoordinator<RetryAttemptKey, RetryOperation, RetryFeedback> _attempts;
    private readonly Dictionary<TraceId, RetryOperation> _operations = [];
    private readonly HashSet<Task> _observations = [];
    private readonly CancellationTokenSource _stopping = new();
    private readonly ActionBlock<FlowMessage> _input;
    private readonly ActionBlock<FlowMessage>[] _feedback;
    private readonly Task _feedbackCompletion;
    private readonly BroadcastBlock<FlowMessage> _events = new(static value => value);
    private readonly Task _completion;
    private readonly string _attemptHeaderName;
    private readonly List<Exception> _failures = [];
    private int _disposed;

    public RetryOperationCoordinator(
        FlowRetryOptions options,
        Func<RetryEmission, CancellationToken, ValueTask<MessageId>> emit,
        TimeProvider? clock = null,
        IRetryJitterSource? jitterSource = null)
    {
        _options = FlowRetryOptionValidation.Validate(options);
        _emit = emit ?? throw new ArgumentNullException(nameof(emit));
        _clock = clock ?? TimeProvider.System;
        _jitterSource = jitterSource ?? RandomRetryJitterSource.Shared;
        _attemptHeaderName = $"flow.retry.attempt.{Guid.NewGuid():N}";
        _attempts = new PendingExchangeCoordinator<RetryAttemptKey, RetryOperation, RetryFeedback>(
            new PendingExchangeCoordinatorOptions
            {
                DefaultTimeout = TimeSpan.FromMilliseconds(_options.AttemptTimeoutMilliseconds),
                MaxPending = _options.Capacity,
                SettledKeyCapacity = Math.Max(_options.Capacity, 4096)
            },
            _clock);
        _input = new ActionBlock<FlowMessage>(
            ProcessInputAsync,
            new ExecutionDataflowBlockOptions
            {
                BoundedCapacity = _options.Capacity,
                EnsureOrdered = true,
                MaxDegreeOfParallelism = 1
            });
        _feedback =
        [
            CreateFeedbackInput(RetryFeedbackKind.Ack),
            CreateFeedbackInput(RetryFeedbackKind.Nak),
            CreateFeedbackInput(RetryFeedbackKind.Cancel)
        ];
        _feedbackCompletion = Task.WhenAll(_feedback.Select(ObserveFeedbackAsync));
        _completion = MonitorAsync();
    }

    public ITargetBlock<FlowMessage> Input => _input;
    public ITargetBlock<FlowMessage> Ack => _feedback[0];
    public ITargetBlock<FlowMessage> Nak => _feedback[1];
    public ITargetBlock<FlowMessage> Cancel => _feedback[2];
    public ISourceBlock<FlowMessage> Events => _events;
    public Task Completion => _completion;
    public void Complete() => _input.Complete();

    public void Fault(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        RecordFatalError(exception);
    }

    private async Task HandleFeedbackAsync(
        TraceId traceId,
        IReadOnlyDictionary<string, string> headers,
        RetryFeedback feedback,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryReadAttempt(headers, out var attempt))
        {
            EmitIgnoredFeedback(traceId, feedback.Kind, "missing-attempt");
            return;
        }

        var result = _attempts.TryResolve(new RetryAttemptKey(traceId, attempt), feedback);
        if (result.Status == PendingExchangeFeedbackStatus.Resolved)
        {
            // Terminal feedback belongs to its native input's processing lifetime.
            // Claim it before that input can drain and shutdown stops other attempts.
            var matched = result.Completion!.Context;
            if (feedback.Kind == RetryFeedbackKind.Ack)
                await CompleteOperationAsync(matched, feedback.MessageId).ConfigureAwait(false);
            else if (feedback.Kind == RetryFeedbackKind.Cancel)
                await CancelOperationAsync(matched, RetryFailureReason.Cancelled, feedback.MessageId).ConfigureAwait(false);
            return;
        }
        if (feedback.Kind == RetryFeedbackKind.Cancel &&
            TryClaimWaitingCancellation(traceId, attempt, out var operation))
        {
            await CancelOperationAsync(operation, RetryFailureReason.Cancelled, feedback.MessageId).ConfigureAwait(false);
            return;
        }

        EmitIgnoredFeedback(traceId, feedback.Kind, result.Status.ToString());
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        Complete();
        try { await Completion.ConfigureAwait(false); }
        catch { }
        finally
        {
            try { await _attempts.DisposeAsync().ConfigureAwait(false); }
            finally { _stopping.Dispose(); }
        }
    }

    private async Task ProcessInputAsync(FlowMessage input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.IsError)
        {
            await EmitEnvelopeAsync(new(input, null, input.Error, input.Headers, input.MessageId)).ConfigureAwait(false);
            return;
        }

        RetryOperation? operation;
        RetryFailureReason rejection;
        lock (_gate)
        {
            if (_operations.ContainsKey(input.TraceId))
            {
                operation = null;
                rejection = RetryFailureReason.Duplicate;
            }
            else if (_operations.Count >= _options.Capacity)
            {
                operation = null;
                rejection = RetryFailureReason.CapacityReached;
            }
            else
            {
                operation = new RetryOperation(
                    input,
                    new RetryStateMachine(FlowRetryOptionValidation.CreatePolicy(_options)),
                    CancellationTokenSource.CreateLinkedTokenSource(_stopping.Token));
                _operations.Add(input.TraceId, operation);
                rejection = RetryFailureReason.None;
            }
        }

        if (operation is null)
        {
            await EmitTerminalAsync(input, 0, _clock.GetUtcNow(), RetrySignalStatus.Rejected,
                rejection, null, input.MessageId).ConfigureAwait(false);
            return;
        }

        await BeginAttemptAsync(operation, operation.StateMachine.Begin(_clock.GetUtcNow())).ConfigureAwait(false);
    }

    private async Task BeginAttemptAsync(RetryOperation operation, RetryDirective directive)
    {
        var key = new RetryAttemptKey(operation.Input.TraceId, directive.Attempt);
        lock (_gate)
        {
            if (operation.Terminal) return;
            operation.State = directive.State;
            operation.CurrentAttempt = directive.Attempt;
        }

        var start = _attempts.TryStart(key, operation,
            TimeSpan.FromMilliseconds(_options.AttemptTimeoutMilliseconds));
        if (start.Status != PendingExchangeStartStatus.Accepted || start.Completion is null)
        {
            var reason = start.Status switch
            {
                PendingExchangeStartStatus.Duplicate => RetryFailureReason.Duplicate,
                PendingExchangeStartStatus.CapacityReached => RetryFailureReason.CapacityReached,
                _ => RetryFailureReason.Stopped
            };
            if (TryClaimTerminal(operation))
                await EmitTerminalAsync(operation.Input, directive.Attempt, directive.State.StartedAt,
                    RetrySignalStatus.Rejected, reason, null, operation.LastMessageId).ConfigureAwait(false);
            return;
        }

        TrackObservation(ObserveAttemptAsync(operation, key, start.Completion));
        var messageId = await EmitAsync(operation.Input, RetrySignalStatus.Attempt, directive.Attempt,
            directive.State.StartedAt, RetryFailureReason.None, null, null,
            operation.LastMessageId).ConfigureAwait(false);
        lock (_gate) operation.LastMessageId = messageId;
        EmitEvent(operation.Input, RetryDiagnosticNames.Attempted, FlowEventLevel.Information,
            directive.Attempt, RetryFailureReason.None, null);
    }

    private async Task ObserveAttemptAsync(
        RetryOperation operation,
        RetryAttemptKey key,
        Task<PendingExchangeCompletion<RetryAttemptKey, RetryOperation, RetryFeedback>> completionTask)
    {
        try
        {
            var completion = await completionTask.ConfigureAwait(false);
            if (!IsCurrent(operation, key.Attempt)) return;
            switch (completion.Kind)
            {
                case PendingExchangeCompletionKind.Resolved when completion.Outcome?.Kind == RetryFeedbackKind.Nak:
                    await RetryOperationAsync(operation, RetryFailureReason.Nak, completion.Outcome.MessageId).ConfigureAwait(false);
                    break;
                case PendingExchangeCompletionKind.TimedOut:
                    await RetryOperationAsync(operation, RetryFailureReason.Timeout, operation.LastMessageId).ConfigureAwait(false);
                    break;
                case PendingExchangeCompletionKind.Stopped:
                case PendingExchangeCompletionKind.Cancelled:
                    await CancelOperationAsync(operation, RetryFailureReason.Stopped, operation.LastMessageId).ConfigureAwait(false);
                    break;
                case PendingExchangeCompletionKind.Faulted:
                    RecordFatalError(completion.Error!);
                    TryClaimTerminal(operation);
                    break;
            }
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested) { }
        catch (Exception exception) { RecordFatalError(exception); }
    }

    private async Task CompleteOperationAsync(RetryOperation operation, MessageId causationId)
    {
        if (!TryClaimTerminal(operation)) return;
        await EmitTerminalAsync(operation.Input, operation.CurrentAttempt, operation.State.StartedAt,
            RetrySignalStatus.Completed, RetryFailureReason.None, null, causationId).ConfigureAwait(false);
        EmitEvent(operation.Input, RetryDiagnosticNames.Completed, FlowEventLevel.Information,
            operation.CurrentAttempt, RetryFailureReason.None, null);
    }

    private async Task RetryOperationAsync(RetryOperation operation, RetryFailureReason reason, MessageId? causationId)
    {
        RetryDirective directive;
        lock (_gate)
        {
            if (operation.Terminal) return;
            directive = operation.StateMachine.AfterFailure(operation.State, _clock.GetUtcNow(), _jitterSource.NextSample());
            operation.State = directive.State;
        }
        if (directive.Kind == RetryDirectiveKind.Exhausted)
        {
            await ExhaustOperationAsync(operation, reason, causationId).ConfigureAwait(false);
            return;
        }

        var delay = directive.Delay > TimeSpan.Zero
            ? Task.Delay(directive.Delay, _clock, operation.Token)
            : Task.CompletedTask;
        var messageId = await EmitAsync(operation.Input, RetrySignalStatus.RetryScheduled,
            operation.CurrentAttempt, operation.State.StartedAt, reason, directive.Delay,
            CreateError(reason, operation.CurrentAttempt, RetrySignalStatus.RetryScheduled, directive.Delay),
            causationId).ConfigureAwait(false);
        lock (_gate) operation.LastMessageId = messageId;
        EmitEvent(operation.Input, RetryDiagnosticNames.Scheduled, FlowEventLevel.Warning,
            operation.CurrentAttempt, reason, directive.Delay);

        try
        {
            await delay.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (operation.Token.IsCancellationRequested)
        {
            await CancelOperationAsync(operation, RetryFailureReason.Stopped, operation.LastMessageId).ConfigureAwait(false);
            return;
        }

        lock (_gate)
        {
            if (operation.Terminal) return;
            directive = operation.StateMachine.AfterDelay(operation.State, _clock.GetUtcNow());
            operation.State = directive.State;
        }
        if (directive.Kind == RetryDirectiveKind.Exhausted)
            await ExhaustOperationAsync(operation, reason, operation.LastMessageId).ConfigureAwait(false);
        else
            await BeginAttemptAsync(operation, directive).ConfigureAwait(false);
    }

    private async Task ExhaustOperationAsync(RetryOperation operation, RetryFailureReason reason, MessageId? causationId)
    {
        if (!TryClaimTerminal(operation)) return;
        await EmitTerminalAsync(operation.Input, operation.CurrentAttempt, operation.State.StartedAt,
            RetrySignalStatus.Exhausted, reason, null, causationId).ConfigureAwait(false);
        EmitEvent(operation.Input, RetryDiagnosticNames.Exhausted, FlowEventLevel.Warning,
            operation.CurrentAttempt, reason, null);
    }

    private async Task CancelOperationAsync(RetryOperation operation, RetryFailureReason reason, MessageId? causationId)
    {
        if (!TryClaimTerminal(operation)) return;
        await EmitTerminalAsync(operation.Input, operation.CurrentAttempt, operation.State.StartedAt,
            RetrySignalStatus.Cancelled, reason, null, causationId).ConfigureAwait(false);
        EmitEvent(operation.Input, RetryDiagnosticNames.Cancelled, FlowEventLevel.Information,
            operation.CurrentAttempt, reason, null);
    }

    private bool TryClaimWaitingCancellation(TraceId traceId, int attempt, out RetryOperation operation)
    {
        lock (_gate)
        {
            if (_operations.TryGetValue(traceId, out operation!) && !operation.Terminal &&
                operation.CurrentAttempt == attempt && operation.State.Status == RetryStateStatus.Waiting)
                return true;
        }
        operation = null!;
        return false;
    }

    private bool TryClaimTerminal(RetryOperation operation)
    {
        lock (_gate)
        {
            if (operation.Terminal) return false;
            operation.Terminal = true;
            _operations.Remove(operation.Input.TraceId);
        }
        try { operation.Cancellation.Cancel(); }
        finally { operation.Cancellation.Dispose(); }
        return true;
    }

    private bool IsCurrent(RetryOperation operation, int attempt)
    {
        lock (_gate) return !operation.Terminal && operation.CurrentAttempt == attempt;
    }

    private bool TryReadAttempt(IReadOnlyDictionary<string, string> headers, out int attempt)
    {
        attempt = 0;
        return headers.TryGetValue(_attemptHeaderName, out var value) &&
            int.TryParse(value, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out attempt) && attempt > 0;
    }

    private Task EmitTerminalAsync(FlowMessage input, int attempt,
        DateTimeOffset startedAt, RetrySignalStatus status, RetryFailureReason reason,
        TimeSpan? nextDelay, MessageId? causationId)
        => EmitAsync(input, status, attempt, startedAt, reason, nextDelay,
            status is RetrySignalStatus.Completed ? null : CreateError(reason, attempt, status, nextDelay),
            causationId);

    private async Task<MessageId> EmitAsync(FlowMessage input,
        RetrySignalStatus status, int attempt, DateTimeOffset startedAt,
        RetryFailureReason reason, TimeSpan? nextDelay, DataFlowError? error,
        MessageId? causationId)
    {
        var signal = new RetrySignal<FlowValue>
        {
            Value = input.Value!,
            Status = status,
            Attempt = attempt,
            StartedAt = startedAt,
            OccurredAt = _clock.GetUtcNow(),
            Reason = reason,
            NextDelay = nextDelay
        };
        var headers = new Dictionary<string, string>(input.Headers, StringComparer.Ordinal)
        {
            [_attemptHeaderName] = attempt.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
        return await EmitEnvelopeAsync(new(input, signal, error, headers,
            causationId ?? input.MessageId)).ConfigureAwait(false);
    }

    private async Task<MessageId> EmitEnvelopeAsync(RetryEmission emission)
    {
        try { return await _emit(emission, _stopping.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested) { throw; }
        catch (Exception exception) { RecordFatalError(exception); throw; }
    }

    private void EmitEvent(FlowMessage input, string name,
        FlowEventLevel level, int attempt, RetryFailureReason reason, TimeSpan? nextDelay)
        => _events.Post(new FlowEvent
        {
            Timestamp = _clock.GetUtcNow(),
            CorrelationId = input.CorrelationId,
            Name = name,
            Level = level,
            Message = $"Retry operation '{_options.Name}' produced {name}.",
            Attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["traceId"] = input.TraceId.ToString(), ["attempt"] = attempt,
                ["reason"] = reason.ToString(), ["nextDelayMilliseconds"] = nextDelay?.TotalMilliseconds
            }
        });

    private void EmitIgnoredFeedback(TraceId traceId, RetryFeedbackKind feedback, string reason)
        => _events.Post(new FlowEvent
        {
            Timestamp = _clock.GetUtcNow(), Name = RetryDiagnosticNames.FeedbackIgnored,
            Level = FlowEventLevel.Warning,
            Message = $"Retry operation '{_options.Name}' ignored {feedback} feedback.",
            Attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["traceId"] = traceId.ToString(), ["feedback"] = feedback.ToString(), ["reason"] = reason
            }
        });

    private static DataFlowError CreateError(RetryFailureReason reason, int attempt,
        RetrySignalStatus status, TimeSpan? nextDelay)
    {
        var code = status == RetrySignalStatus.Exhausted ? RetryErrorCodeNames.Exhausted : reason switch
        {
            RetryFailureReason.Nak => RetryErrorCodeNames.Nak,
            RetryFailureReason.Timeout => RetryErrorCodeNames.Timeout,
            RetryFailureReason.Cancelled => RetryErrorCodeNames.Cancelled,
            RetryFailureReason.Duplicate => RetryErrorCodeNames.Duplicate,
            RetryFailureReason.CapacityReached => RetryErrorCodeNames.CapacityReached,
            RetryFailureReason.Stopped => RetryErrorCodeNames.Stopped,
            _ => RetryErrorCodeNames.Nak
        };
        var terminal = status is not RetrySignalStatus.RetryScheduled;
        return new DataFlowError(code,
            terminal ? $"Retry operation ended after attempt {attempt}: {reason}." :
                $"Retry attempt {attempt} did not complete successfully: {reason}.",
            category: "Resilience", isTransient: !terminal,
            details: JsonSerializer.SerializeToElement(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["attempt"] = attempt, ["nextDelayMilliseconds"] = nextDelay?.TotalMilliseconds,
                ["reason"] = reason.ToString()
            }));
    }

    private void TrackObservation(Task task)
    {
        lock (_gate) _observations.Add(task);
        _ = task.ContinueWith(completed => { lock (_gate) _observations.Remove(completed); },
            CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private async Task DrainObservationsAsync()
    {
        while (true)
        {
            Task[] pending;
            lock (_gate) pending = [.. _observations];
            if (pending.Length == 0) return;
            await Task.WhenAll(pending).ConfigureAwait(false);
        }
    }

    private void RecordFatalError(Exception exception)
    {
        RecordFailure(exception);
        try { _stopping.Cancel(); }
        catch (Exception error) { RecordFailure(error); }
        try { _attempts.Stop(exception); }
        catch (Exception error) { RecordFailure(error); }
        ((IDataflowBlock)_input).Fault(exception);
        foreach (var input in _feedback) ((IDataflowBlock)input).Fault(exception);
    }

    private ActionBlock<FlowMessage> CreateFeedbackInput(RetryFeedbackKind kind)
        => new(async message =>
        {
            ArgumentNullException.ThrowIfNull(message);
            await HandleFeedbackAsync(message.TraceId, message.Headers,
                new RetryFeedback(kind, message.MessageId), _stopping.Token).ConfigureAwait(false);
        }, new ExecutionDataflowBlockOptions
        {
            BoundedCapacity = _options.Capacity, EnsureOrdered = true, MaxDegreeOfParallelism = 1
        });

    private async Task ObserveFeedbackAsync(ActionBlock<FlowMessage> input)
    {
        try { await input.Completion.ConfigureAwait(false); }
        catch (Exception error) { RecordFatalError(error); }
    }

    private void RecordFailure(Exception exception)
    {
        if (exception is AggregateException aggregate)
        {
            foreach (var error in aggregate.InnerExceptions) RecordFailure(error);
            return;
        }
        lock (_gate)
            if (!_failures.Contains(exception)) _failures.Add(exception);
    }

    private async Task MonitorAsync()
    {
        try { await _input.Completion.ConfigureAwait(false); }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested) { }
        catch (Exception error) { RecordFatalError(error); }

        // Feedback is processing work, not terminal control. Join every admitted
        // callback before stopping attempts or completing the result stream.
        foreach (var input in _feedback) input.Complete();
        await _feedbackCompletion.ConfigureAwait(false);

        try { _attempts.Stop(); }
        catch (Exception error) { RecordFailure(error); }

        // Input has ended: also terminate operations between attempts before joining
        // their delay observers. Graceful shutdown can still emit terminal results.
        RetryOperation[] remaining;
        lock (_gate) remaining = _operations.Values.ToArray();
        foreach (var operation in remaining)
        {
            try { await CancelOperationAsync(operation, RetryFailureReason.Stopped, operation.LastMessageId).ConfigureAwait(false); }
            catch (OperationCanceledException) when (_stopping.IsCancellationRequested) { }
            catch (Exception error) { RecordFailure(error); }
        }
        try { await DrainObservationsAsync().ConfigureAwait(false); }
        catch (Exception error) { RecordFailure(error); }
        try { _stopping.Cancel(); }
        catch (Exception error) { RecordFailure(error); }
        _events.Complete();
        try { await _events.Completion.ConfigureAwait(false); }
        catch (Exception error) { RecordFailure(error); }

        Exception[] failures;
        lock (_gate) failures = _failures.ToArray();
        if (failures.Length == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Length > 1) throw new AggregateException(failures);
    }

    private sealed class RetryOperation(FlowMessage input,
        RetryStateMachine stateMachine, CancellationTokenSource cancellation)
    {
        public FlowMessage Input { get; } = input;
        public RetryStateMachine StateMachine { get; } = stateMachine;
        public CancellationTokenSource Cancellation { get; } = cancellation;
        public CancellationToken Token { get; } = cancellation.Token;
        public RetryState State { get; set; }
        public int CurrentAttempt { get; set; }
        public MessageId? LastMessageId { get; set; } = input.MessageId;
        public bool Terminal { get; set; }
    }
}
