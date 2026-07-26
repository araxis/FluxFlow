using System.Text.Json;
using FluxFlow.Components.Resilience.Contracts;
using FluxFlow.Components.Resilience.Diagnostics;
using FluxFlow.Components.Resilience.Options;
using FluxFlow.Coordination;
using FluxFlow.Data;
using FluxFlow.Nodes;
using FluxFlow.Resilience;
using System.Threading.Tasks.Dataflow;

using DataFlowError = FluxFlow.Data.FlowError;

namespace FluxFlow.Components.Resilience.Nodes;

public sealed class FlowRetryNode : FlowRetryNode<JsonElement>
{
    public FlowRetryNode(
        FlowRetryOptions? options = null,
        TimeProvider? clock = null,
        IRetryJitterSource? jitterSource = null)
        : base(options, clock, jitterSource)
    {
    }
}

public class FlowRetryNode<T> : IFlowNode
{
    private readonly object _gate = new();
    private readonly FlowRetryOptions _options;
    private readonly TimeProvider _clock;
    private readonly IRetryJitterSource _jitterSource;
    private readonly PendingExchangeCoordinator<RetryAttemptKey, RetryOperation, RetryFeedback> _attempts;
    private readonly Dictionary<TraceId, RetryOperation> _operations = [];
    private readonly HashSet<Task> _observations = [];
    private readonly CancellationTokenSource _stopping = new();
    private readonly ActionBlock<FlowMessage<T>> _input;
    private readonly BroadcastBlock<FlowMessage<RetrySignal<T>>> _output;
    private readonly BroadcastBlock<FlowEvent> _events = new(static @event => @event);
    private readonly TaskCompletionSource _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly string _attemptHeaderName;
    private Exception? _fatalError;
    private int _disposed;

    public FlowRetryNode(
        FlowRetryOptions? options = null,
        TimeProvider? clock = null,
        IRetryJitterSource? jitterSource = null)
    {
        _options = FlowRetryOptionValidation.Validate(options);
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
        _output = new BroadcastBlock<FlowMessage<RetrySignal<T>>>(
            static message => message,
            new DataflowBlockOptions { BoundedCapacity = _options.Capacity });
        _input = new ActionBlock<FlowMessage<T>>(
            ProcessInputAsync,
            new ExecutionDataflowBlockOptions
            {
                BoundedCapacity = _options.Capacity,
                EnsureOrdered = true,
                MaxDegreeOfParallelism = 1
            });

        Ack = new RetrySignalTarget(HandleFeedbackAsync, Completion, RetryFeedbackKind.Ack);
        Nak = new RetrySignalTarget(HandleFeedbackAsync, Completion, RetryFeedbackKind.Nak);
        Cancel = new RetrySignalTarget(HandleFeedbackAsync, Completion, RetryFeedbackKind.Cancel);
        _ = MonitorAsync();
    }

    public ITargetBlock<FlowMessage<T>> Input => _input;

    public IFlowSignalTarget Ack { get; }

    public IFlowSignalTarget Nak { get; }

    public IFlowSignalTarget Cancel { get; }

    public ISourceBlock<FlowMessage<RetrySignal<T>>> Output => _output;

    public ISourceBlock<FlowEvent> Events => _events;

    public Task Completion => _completion.Task;

    public void Complete() => _input.Complete();

    public void Fault(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        RecordFatalError(exception);
        ((IDataflowBlock)_input).Fault(exception);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        Complete();
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
            await _attempts.DisposeAsync().ConfigureAwait(false);
            _stopping.Dispose();
        }
    }

    private async Task ProcessInputAsync(FlowMessage<T> message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.IsError)
        {
            await _output.SendAsync(message.WithError<RetrySignal<T>>(message.Error!))
                .ConfigureAwait(false);
            return;
        }

        RetryOperation? operation;
        RetryFailureReason rejection;
        lock (_gate)
        {
            if (_operations.ContainsKey(message.TraceId))
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
                    message,
                    new RetryStateMachine(FlowRetryOptionValidation.CreatePolicy(_options)),
                    CancellationTokenSource.CreateLinkedTokenSource(_stopping.Token));
                _operations.Add(message.TraceId, operation);
                rejection = RetryFailureReason.None;
            }
        }

        if (operation is null)
        {
            await EmitTerminalAsync(
                message,
                attempt: 0,
                startedAt: _clock.GetUtcNow(),
                RetrySignalStatus.Rejected,
                RetryResultKinds.Rejected,
                rejection,
                nextDelay: null,
                causationId: message.MessageId).ConfigureAwait(false);
            return;
        }

        var directive = operation.StateMachine.Begin(_clock.GetUtcNow());
        await BeginAttemptAsync(operation, directive).ConfigureAwait(false);
    }

    private async Task BeginAttemptAsync(RetryOperation operation, RetryDirective directive)
    {
        var key = new RetryAttemptKey(operation.Message.TraceId, directive.Attempt);
        lock (_gate)
        {
            if (operation.Terminal)
                return;
            operation.State = directive.State;
            operation.CurrentAttempt = directive.Attempt;
        }

        var start = _attempts.TryStart(
            key,
            operation,
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
            {
                await EmitTerminalAsync(
                    operation.Message,
                    directive.Attempt,
                    directive.State.StartedAt,
                    RetrySignalStatus.Rejected,
                    RetryResultKinds.Rejected,
                    reason,
                    nextDelay: null,
                    causationId: operation.LastMessageId).ConfigureAwait(false);
            }
            return;
        }

        TrackObservation(ObserveAttemptAsync(operation, key, start.Completion));
        var attemptOutput = await EmitAsync(
            operation.Message,
            RetryResultKinds.Attempt,
            RetrySignalStatus.Attempt,
            directive.Attempt,
            directive.State.StartedAt,
            RetryFailureReason.None,
            nextDelay: null,
            error: null,
            causationId: operation.LastMessageId).ConfigureAwait(false);
        lock (_gate)
            operation.LastMessageId = attemptOutput.MessageId;
        EmitEvent(
            operation.Message,
            RetryDiagnosticNames.Attempted,
            FlowEventLevel.Information,
            directive.Attempt,
            RetryFailureReason.None,
            nextDelay: null);
    }

    private async Task ObserveAttemptAsync(
        RetryOperation operation,
        RetryAttemptKey key,
        Task<PendingExchangeCompletion<RetryAttemptKey, RetryOperation, RetryFeedback>> completionTask)
    {
        try
        {
            var completion = await completionTask.ConfigureAwait(false);
            if (!IsCurrent(operation, key.Attempt))
                return;

            switch (completion.Kind)
            {
                case PendingExchangeCompletionKind.Resolved when completion.Outcome?.Kind == RetryFeedbackKind.Ack:
                    await CompleteOperationAsync(operation, completion.Outcome.MessageId).ConfigureAwait(false);
                    break;
                case PendingExchangeCompletionKind.Resolved when completion.Outcome?.Kind == RetryFeedbackKind.Cancel:
                    await CancelOperationAsync(operation, RetryFailureReason.Cancelled, completion.Outcome.MessageId).ConfigureAwait(false);
                    break;
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
                    break;
            }
        }
        catch (Exception exception)
        {
            RecordFatalError(exception);
        }
    }

    private async Task CompleteOperationAsync(RetryOperation operation, MessageId causationId)
    {
        if (!TryClaimTerminal(operation))
            return;

        await EmitTerminalAsync(
            operation.Message,
            operation.CurrentAttempt,
            operation.State.StartedAt,
            RetrySignalStatus.Completed,
            RetryResultKinds.Completed,
            RetryFailureReason.None,
            nextDelay: null,
            causationId).ConfigureAwait(false);
        EmitEvent(
            operation.Message,
            RetryDiagnosticNames.Completed,
            FlowEventLevel.Information,
            operation.CurrentAttempt,
            RetryFailureReason.None,
            nextDelay: null);
    }

    private async Task RetryOperationAsync(
        RetryOperation operation,
        RetryFailureReason reason,
        MessageId? causationId)
    {
        RetryDirective directive;
        lock (_gate)
        {
            if (operation.Terminal)
                return;
            directive = operation.StateMachine.AfterFailure(
                operation.State,
                _clock.GetUtcNow(),
                _jitterSource.NextSample());
            operation.State = directive.State;
        }

        if (directive.Kind == RetryDirectiveKind.Exhausted)
        {
            await ExhaustOperationAsync(operation, reason, causationId).ConfigureAwait(false);
            return;
        }

        var scheduledOutput = await EmitAsync(
            operation.Message,
            RetryResultKinds.Scheduled,
            RetrySignalStatus.RetryScheduled,
            operation.CurrentAttempt,
            operation.State.StartedAt,
            reason,
            directive.Delay,
            CreateError(reason, operation.CurrentAttempt, RetrySignalStatus.RetryScheduled, directive.Delay),
            causationId).ConfigureAwait(false);
        lock (_gate)
            operation.LastMessageId = scheduledOutput.MessageId;
        EmitEvent(
            operation.Message,
            RetryDiagnosticNames.Scheduled,
            FlowEventLevel.Warning,
            operation.CurrentAttempt,
            reason,
            directive.Delay);

        try
        {
            if (directive.Delay > TimeSpan.Zero)
            {
                await Task.Delay(
                    directive.Delay,
                    _clock,
                    operation.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (operation.Token.IsCancellationRequested)
        {
            await CancelOperationAsync(
                operation,
                RetryFailureReason.Stopped,
                operation.LastMessageId).ConfigureAwait(false);
            return;
        }

        lock (_gate)
        {
            if (operation.Terminal)
                return;
            directive = operation.StateMachine.AfterDelay(operation.State, _clock.GetUtcNow());
            operation.State = directive.State;
        }

        if (directive.Kind == RetryDirectiveKind.Exhausted)
        {
            await ExhaustOperationAsync(operation, reason, operation.LastMessageId).ConfigureAwait(false);
            return;
        }

        await BeginAttemptAsync(operation, directive).ConfigureAwait(false);
    }

    private async Task ExhaustOperationAsync(
        RetryOperation operation,
        RetryFailureReason reason,
        MessageId? causationId)
    {
        if (!TryClaimTerminal(operation))
            return;

        await EmitTerminalAsync(
            operation.Message,
            operation.CurrentAttempt,
            operation.State.StartedAt,
            RetrySignalStatus.Exhausted,
            RetryResultKinds.Exhausted,
            reason,
            nextDelay: null,
            causationId).ConfigureAwait(false);
        EmitEvent(
            operation.Message,
            RetryDiagnosticNames.Exhausted,
            FlowEventLevel.Warning,
            operation.CurrentAttempt,
            reason,
            nextDelay: null);
    }

    private async Task CancelOperationAsync(
        RetryOperation operation,
        RetryFailureReason reason,
        MessageId? causationId)
    {
        if (!TryClaimTerminal(operation))
            return;

        await EmitTerminalAsync(
            operation.Message,
            operation.CurrentAttempt,
            operation.State.StartedAt,
            RetrySignalStatus.Cancelled,
            RetryResultKinds.Cancelled,
            reason,
            nextDelay: null,
            causationId).ConfigureAwait(false);
        EmitEvent(
            operation.Message,
            RetryDiagnosticNames.Cancelled,
            FlowEventLevel.Information,
            operation.CurrentAttempt,
            reason,
            nextDelay: null);
    }

    private async ValueTask<bool> HandleFeedbackAsync(
        TraceId traceId,
        IReadOnlyDictionary<string, string> headers,
        RetryFeedback feedback,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryReadAttempt(headers, out var attempt))
        {
            EmitIgnoredFeedback(traceId, feedback.Kind, "missing-attempt");
            return false;
        }

        var key = new RetryAttemptKey(traceId, attempt);
        var result = _attempts.TryResolve(key, feedback);
        if (result.Status == PendingExchangeFeedbackStatus.Resolved)
            return true;

        if (feedback.Kind == RetryFeedbackKind.Cancel && TryClaimWaitingCancellation(traceId, attempt, out var operation))
        {
            await CancelOperationAsync(operation, RetryFailureReason.Cancelled, feedback.MessageId).ConfigureAwait(false);
            return true;
        }

        EmitIgnoredFeedback(traceId, feedback.Kind, result.Status.ToString());
        return false;
    }

    private bool TryClaimWaitingCancellation(TraceId traceId, int attempt, out RetryOperation operation)
    {
        lock (_gate)
        {
            if (_operations.TryGetValue(traceId, out operation!) &&
                !operation.Terminal &&
                operation.CurrentAttempt == attempt &&
                operation.State.Status == RetryStateStatus.Waiting)
            {
                return true;
            }
        }

        operation = null!;
        return false;
    }

    private bool TryClaimTerminal(RetryOperation operation)
    {
        lock (_gate)
        {
            if (operation.Terminal)
                return false;
            operation.Terminal = true;
            _operations.Remove(operation.Message.TraceId);
        }

        operation.Cancellation.Cancel();
        operation.Cancellation.Dispose();
        return true;
    }

    private bool IsCurrent(RetryOperation operation, int attempt)
    {
        lock (_gate)
            return !operation.Terminal && operation.CurrentAttempt == attempt;
    }

    private bool TryReadAttempt(IReadOnlyDictionary<string, string> headers, out int attempt)
    {
        attempt = 0;
        if (!headers.TryGetValue(_attemptHeaderName, out var value))
            return false;
        return int.TryParse(
            value,
            System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture,
            out attempt) && attempt > 0;
    }

    private Task EmitTerminalAsync(
        FlowMessage<T> message,
        int attempt,
        DateTimeOffset startedAt,
        RetrySignalStatus status,
        string resultKind,
        RetryFailureReason reason,
        TimeSpan? nextDelay,
        MessageId? causationId)
        => EmitAsync(
            message,
            resultKind,
            status,
            attempt,
            startedAt,
            reason,
            nextDelay,
            status is RetrySignalStatus.Completed
                ? null
                : CreateError(reason, attempt, status, nextDelay),
            causationId);

    private async Task<FlowMessage<RetrySignal<T>>> EmitAsync(
        FlowMessage<T> message,
        string resultKind,
        RetrySignalStatus status,
        int attempt,
        DateTimeOffset startedAt,
        RetryFailureReason reason,
        TimeSpan? nextDelay,
        DataFlowError? error,
        MessageId? causationId)
    {
        var now = _clock.GetUtcNow();
        var signal = new RetrySignal<T>
        {
            Value = message.Value,
            Status = status,
            Attempt = attempt,
            StartedAt = startedAt,
            OccurredAt = now,
            Reason = reason,
            NextDelay = nextDelay
        };
        var headers = new Dictionary<string, string>(message.Headers, StringComparer.Ordinal)
        {
            [_attemptHeaderName] = attempt.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
        var output = error is null
            ? message.With(signal, headers, causationId ?? message.MessageId)
            : message.WithError<RetrySignal<T>>(error, headers, causationId ?? message.MessageId);
        if (!await _output.SendAsync(output).ConfigureAwait(false))
            throw new InvalidOperationException("Retry output declined an accepted result.");
        return output;
    }

    private void EmitEvent(
        FlowMessage<T> message,
        string name,
        FlowEventLevel level,
        int attempt,
        RetryFailureReason reason,
        TimeSpan? nextDelay)
        => _events.Post(new FlowEvent
        {
            Timestamp = _clock.GetUtcNow(),
            CorrelationId = message.CorrelationId,
            Name = name,
            Level = level,
            Message = $"Retry operation '{_options.Name}' produced {name}.",
            Attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["traceId"] = message.TraceId.ToString(),
                ["attempt"] = attempt,
                ["reason"] = reason.ToString(),
                ["nextDelayMilliseconds"] = nextDelay?.TotalMilliseconds
            }
        });

    private void EmitIgnoredFeedback(TraceId traceId, RetryFeedbackKind feedback, string reason)
        => _events.Post(new FlowEvent
        {
            Timestamp = _clock.GetUtcNow(),
            Name = RetryDiagnosticNames.FeedbackIgnored,
            Level = FlowEventLevel.Warning,
            Message = $"Retry operation '{_options.Name}' ignored {feedback} feedback.",
            Attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["traceId"] = traceId.ToString(),
                ["feedback"] = feedback.ToString(),
                ["reason"] = reason
            }
        });

    private static DataFlowError CreateError(
        RetryFailureReason reason,
        int attempt,
        RetrySignalStatus status,
        TimeSpan? nextDelay)
    {
        var code = status == RetrySignalStatus.Exhausted
            ? RetryErrorCodeNames.Exhausted
            : reason switch
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
        var message = terminal
            ? $"Retry operation ended after attempt {attempt}: {reason}."
            : $"Retry attempt {attempt} did not complete successfully: {reason}.";
        return new DataFlowError(
            code,
            message,
            category: "Resilience",
            isTransient: !terminal,
            details: JsonSerializer.SerializeToElement(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["attempt"] = attempt,
                ["nextDelayMilliseconds"] = nextDelay?.TotalMilliseconds,
                ["reason"] = reason.ToString()
            }));
    }

    private void TrackObservation(Task task)
    {
        lock (_gate)
            _observations.Add(task);
        _ = task.ContinueWith(
            completed =>
            {
                lock (_gate)
                    _observations.Remove(completed);
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private async Task DrainObservationsAsync()
    {
        while (true)
        {
            Task[] pending;
            lock (_gate)
                pending = [.. _observations];
            if (pending.Length == 0)
                return;
            await Task.WhenAll(pending).ConfigureAwait(false);
        }
    }

    private void RecordFatalError(Exception exception)
    {
        lock (_gate)
            _fatalError ??= exception;
        _stopping.Cancel();
        _attempts.Stop(exception);
        ((IDataflowBlock)_input).Fault(exception);
    }

    private async Task MonitorAsync()
    {
        Exception? inputError = null;
        try
        {
            await _input.Completion.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            inputError = exception;
        }

        _stopping.Cancel();
        _attempts.Stop(inputError);
        await DrainObservationsAsync().ConfigureAwait(false);

        Exception? fatal;
        lock (_gate)
            fatal = _fatalError ?? inputError;
        if (fatal is null)
        {
            _output.Complete();
            _events.Complete();
            await Task.WhenAll(_output.Completion, _events.Completion).ConfigureAwait(false);
            _completion.TrySetResult();
            return;
        }

        ((IDataflowBlock)_output).Fault(fatal);
        _events.Complete();
        _completion.TrySetException(fatal);
    }

    private sealed class RetryOperation(
        FlowMessage<T> message,
        RetryStateMachine stateMachine,
        CancellationTokenSource cancellation)
    {
        public FlowMessage<T> Message { get; } = message;

        public RetryStateMachine StateMachine { get; } = stateMachine;

        public CancellationTokenSource Cancellation { get; } = cancellation;

        public CancellationToken Token { get; } = cancellation.Token;

        public RetryState State { get; set; }

        public int CurrentAttempt { get; set; }

        public MessageId? LastMessageId { get; set; } = message.MessageId;

        public bool Terminal { get; set; }
    }
}
