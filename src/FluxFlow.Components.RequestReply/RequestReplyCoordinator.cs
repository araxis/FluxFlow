using FluxFlow.Nodes;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.RequestReply;

/// <summary>
/// Bridges request/reply onto a one-way dataflow graph. The host feeds inbound
/// <see cref="IRequestContext{TRequest,TResponse}"/> values into <see cref="Incoming"/>;
/// the bridge mints (or honours) a <see cref="CorrelationId"/>, holds the context
/// in-flight, and broadcasts the request on <see cref="Output"/>. The graph processes
/// it and posts the correlated response (same id, preserved via
/// <see cref="FlowMessage{T}.With{TOut}"/>) to <see cref="Responses"/>; the bridge
/// matches by id and replies through the context. In-flight requests that get no
/// response within the timeout are failed and evicted. The bridge knows nothing about
/// the transport — that lives entirely in the context's <c>ReplyAsync</c>/<c>FailAsync</c>.
/// </summary>
public sealed class RequestReplyCoordinator<TRequest, TResponse> : IFlowNode
{
    private readonly RequestReplyOptions _options;
    private readonly TimeProvider _clock;
    private readonly ActionBlock<IRequestContext<TRequest, TResponse>> _incoming;
    private readonly ActionBlock<FlowMessage<TResponse>> _responses;
    private readonly BufferBlock<FlowMessage<TRequest>> _output;
    private readonly BroadcastBlock<FlowError> _errors;
    private readonly BroadcastBlock<FlowEvent> _events;
    private readonly CorrelatedRequestTracker<IRequestContext<TRequest, TResponse>, TResponse>? _tracker;
    private readonly CancellationTokenSource _stopping = new();
    private int _disposed;

    public RequestReplyCoordinator(RequestReplyOptions? options = null, TimeProvider? clock = null)
    {
        _options = options ?? new RequestReplyOptions();
        ValidateOptions(_options);

        _clock = clock ?? TimeProvider.System;
        _tracker = _options.Mode == RequestReplyMode.RequestReply
            ? new CorrelatedRequestTracker<IRequestContext<TRequest, TResponse>, TResponse>(
                OnTrackedResponseAsync,
                OnTrackedFailureAsync,
                new CorrelatedRequestTrackerOptions
                {
                    Timeout = _options.Timeout,
                    SweepInterval = _options.SweepInterval
                },
                _clock)
            : null;

        _output = new BufferBlock<FlowMessage<TRequest>>(new DataflowBlockOptions
        {
            BoundedCapacity = _options.Capacity
        });
        _errors = new BroadcastBlock<FlowError>(static value => value);
        _events = new BroadcastBlock<FlowEvent>(static value => value);

        _incoming = new ActionBlock<IRequestContext<TRequest, TResponse>>(
            OnIncomingAsync,
            new ExecutionDataflowBlockOptions
            {
                BoundedCapacity = _options.Capacity,
                MaxDegreeOfParallelism = 1,
                EnsureOrdered = true
            });
        _responses = new ActionBlock<FlowMessage<TResponse>>(
            OnResponseAsync,
            new ExecutionDataflowBlockOptions
            {
                BoundedCapacity = _options.Capacity,
                MaxDegreeOfParallelism = 1,
                EnsureOrdered = true
            });

        // When the host stops feeding requests, stop offering them to the graph.
        _ = _incoming.Completion.ContinueWith(
            _ => _output.Complete(),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    /// <summary>Inbound request contexts — the host posts here (bounded; backpressure).</summary>
    public ITargetBlock<IRequestContext<TRequest, TResponse>> Incoming => _incoming;

    /// <summary>Requests into the graph — reliable bounded delivery (no dropped requests).</summary>
    public ISourceBlock<FlowMessage<TRequest>> Output => _output;

    /// <summary>Correlated responses from the graph — link the graph's output here.</summary>
    public ITargetBlock<FlowMessage<TResponse>> Responses => _responses;

    public ISourceBlock<FlowError> Errors => _errors;

    public ISourceBlock<FlowEvent> Events => _events;

    /// <summary>In-flight request count (requests awaiting a response).</summary>
    public int InFlightCount => _tracker?.PendingCount ?? 0;

    public Task Completion => Task.WhenAll(
        _incoming.Completion,
        _responses.Completion,
        _output.Completion,
        _errors.Completion,
        _events.Completion);

    public void Complete()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        CompleteBlocks();
        _ = FailInFlightAsync(new OperationCanceledException(
                "The request/reply bridge was completed."))
            .AsTask();
    }

    public void Fault(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        _stopping.Cancel();

        // Fail anything still in flight so no caller hangs forever.
        if (_tracker is not null)
        {
            _ = _tracker.FailAllAsync(exception).AsTask();
        }

        // Fault the data blocks so Completion surfaces the fault. Flush — not fault —
        // the diagnostic ports so buffered Errors/Events survive, matching the kit's
        // fault rule.
        ((IDataflowBlock)_output).Fault(exception);
        ((IDataflowBlock)_incoming).Fault(exception);
        ((IDataflowBlock)_responses).Fault(exception);
        _errors.Complete();
        _events.Complete();
    }

    private async Task OnIncomingAsync(IRequestContext<TRequest, TResponse> context)
    {
        var id = context.CorrelationId ?? CorrelationId.New();

        if (_options.Mode == RequestReplyMode.FireAndForget)
        {
            // Correlate + publish, then acknowledge the caller immediately — no in-flight
            // tracking, no waiting for a graph response.
            EmitEvent(RequestReplyEvents.Received, id);

            bool published;
            try
            {
                published = await _output.SendAsync(FlowMessage.Create(context.Request, id), _stopping.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                published = false;
            }

            if (!published)
            {
                await SafeFailAsync(context, new InvalidOperationException(
                    "The request/reply bridge is shutting down.")).ConfigureAwait(false);
                return;
            }

            await SafeAcknowledgeAsync(context).ConfigureAwait(false);
            EmitEvent(RequestReplyEvents.Published, id);
            return;
        }

        var startResult = _tracker!.TryAdd(id, context);
        if (startResult == CorrelatedRequestStartResult.DuplicateCorrelationId)
        {
            await SafeFailAsync(context, new InvalidOperationException(
                $"A request with correlation id '{id}' is already in flight.")).ConfigureAwait(false);
            EmitError(RequestReplyErrorCodes.DuplicateCorrelationId,
                $"Duplicate correlation id '{id}'.", id);
            return;
        }

        if (startResult == CorrelatedRequestStartResult.Stopped)
        {
            await SafeFailAsync(context, new InvalidOperationException(
                "The request/reply bridge is shutting down.")).ConfigureAwait(false);
            return;
        }

        EmitEvent(RequestReplyEvents.Received, id);

        bool accepted;
        try
        {
            accepted = await _output.SendAsync(FlowMessage.Create(context.Request, id), _stopping.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            accepted = false;
        }

        if (!accepted && _tracker.TryRemove(id, out var removed))
        {
            // Output closed (shutdown) before the request reached the graph.
            await SafeFailAsync(removed, new InvalidOperationException(
                "The request/reply bridge is shutting down.")).ConfigureAwait(false);
        }
    }

    private async Task OnResponseAsync(FlowMessage<TResponse> message)
    {
        if (_tracker is null
            || !await _tracker.TryCompleteAsync(message, _stopping.Token).ConfigureAwait(false))
        {
            // No matching request: late/duplicate response or already timed out.
            EmitError(RequestReplyErrorCodes.Unmatched,
                "Received a response with no matching in-flight request.", message.CorrelationId);
            EmitEvent(RequestReplyEvents.Unmatched, message.CorrelationId, FlowEventLevel.Warning);
            return;
        }
    }

    private async ValueTask OnTrackedResponseAsync(
        CorrelationId correlationId,
        IRequestContext<TRequest, TResponse> context,
        FlowMessage<TResponse> message,
        CancellationToken cancellationToken)
    {
        try
        {
            await context.ReplyAsync(message.Payload, cancellationToken).ConfigureAwait(false);
            EmitEvent(RequestReplyEvents.Replied, correlationId);
        }
        catch (Exception exception)
        {
            EmitError(RequestReplyErrorCodes.ReplyFailed,
                $"Failed to reply: {exception.Message}", correlationId, exception);
        }
    }

    private async ValueTask OnTrackedFailureAsync(
        CorrelationId correlationId,
        IRequestContext<TRequest, TResponse> context,
        Exception exception,
        CancellationToken cancellationToken)
    {
        await SafeFailAsync(context, exception, cancellationToken).ConfigureAwait(false);

        if (exception is TimeoutException)
        {
            EmitError(RequestReplyErrorCodes.TimedOut,
                $"Request '{correlationId}' timed out after {_options.Timeout.TotalMilliseconds:0} ms.",
                correlationId,
                exception);
            EmitEvent(RequestReplyEvents.TimedOut, correlationId, FlowEventLevel.Warning);
        }
    }

    private static async Task SafeFailAsync(
        IRequestContext<TRequest, TResponse> context,
        Exception error,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await context.FailAsync(error, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // The caller may already be gone (aborted); failing to fail is not fatal.
        }
    }

    private static async Task SafeAcknowledgeAsync(IRequestContext<TRequest, TResponse> context)
    {
        try
        {
            await context.AcknowledgeAsync().ConfigureAwait(false);
        }
        catch
        {
            // The caller may already be gone (aborted); failing to acknowledge is not fatal.
        }
    }

    private void EmitEvent(string name, CorrelationId id, FlowEventLevel level = FlowEventLevel.Information)
        => _events.Post(new FlowEvent
        {
            Timestamp = _clock.GetUtcNow(),
            CorrelationId = id,
            Name = name,
            Level = level
        });

    private void EmitError(int code, string message, CorrelationId id, Exception? exception = null)
        => _errors.Post(new FlowError
        {
            Timestamp = _clock.GetUtcNow(),
            CorrelationId = id,
            Code = code,
            Message = message,
            Exception = exception
        });

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        CompleteBlocks();
        await FailInFlightAsync(new OperationCanceledException(
                "The request/reply bridge was disposed."))
            .ConfigureAwait(false);
        try
        {
            await Completion.ConfigureAwait(false);
        }
        catch
        {
            // Completion may surface a fault; disposal must still release resources.
        }

        if (_tracker is not null)
        {
            await _tracker.DisposeAsync().ConfigureAwait(false);
        }

        _stopping.Dispose();
    }

    private void CompleteBlocks()
    {
        _incoming.Complete();
        _responses.Complete();
        _output.Complete();
        _errors.Complete();
        _events.Complete();
        _stopping.Cancel();
    }

    private ValueTask FailInFlightAsync(Exception exception)
        => _tracker?.FailAllAsync(exception) ?? ValueTask.CompletedTask;

    private static void ValidateOptions(RequestReplyOptions options)
    {
        if (!Enum.IsDefined(options.Mode))
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.Mode,
                "Request/reply mode is not supported.");
        }

        if (options.Capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.Capacity,
                "Capacity must be greater than zero.");
        }

        if (options.Timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.Timeout,
                "Timeout must be greater than zero.");
        }

        if (options.SweepInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.SweepInterval,
                "Sweep interval must be greater than zero.");
        }
    }
}
