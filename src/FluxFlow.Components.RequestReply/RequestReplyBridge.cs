using FluxFlow.Nodes;
using System.Collections.Concurrent;
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
public sealed class RequestReplyBridge<TRequest, TResponse> : IAsyncDisposable
{
    private readonly RequestReplyOptions _options;
    private readonly TimeProvider _clock;
    private readonly ActionBlock<IRequestContext<TRequest, TResponse>> _incoming;
    private readonly ActionBlock<FlowMessage<TResponse>> _responses;
    private readonly BufferBlock<FlowMessage<TRequest>> _output;
    private readonly BroadcastBlock<FlowError> _errors;
    private readonly BroadcastBlock<FlowEvent> _events;
    private readonly ConcurrentDictionary<CorrelationId, InFlight> _inFlight = new();
    private readonly ITimer _sweep;
    private readonly CancellationTokenSource _stopping = new();
    private int _disposed;

    public RequestReplyBridge(RequestReplyOptions? options = null, TimeProvider? clock = null)
    {
        _options = options ?? new RequestReplyOptions();
        if (_options.Capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Capacity must be greater than zero.");
        }

        if (_options.Timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Timeout must be greater than zero.");
        }

        _clock = clock ?? TimeProvider.System;

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

        _sweep = _clock.CreateTimer(_ => Sweep(), null, _options.SweepInterval, _options.SweepInterval);
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
    public int InFlightCount => _inFlight.Count;

    public Task Completion => Task.WhenAll(_incoming.Completion, _responses.Completion);

    public void Complete() => _incoming.Complete();

    private async Task OnIncomingAsync(IRequestContext<TRequest, TResponse> context)
    {
        var id = context.CorrelationId ?? CorrelationId.New();
        var entry = new InFlight(context, _clock.GetUtcNow() + _options.Timeout);
        if (!_inFlight.TryAdd(id, entry))
        {
            await SafeFailAsync(context, new InvalidOperationException(
                $"A request with correlation id '{id}' is already in flight.")).ConfigureAwait(false);
            EmitError(RequestReplyErrorCodes.DuplicateCorrelationId,
                $"Duplicate correlation id '{id}'.", id);
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

        if (!accepted && _inFlight.TryRemove(id, out _))
        {
            // Output closed (shutdown) before the request reached the graph.
            await SafeFailAsync(context, new InvalidOperationException(
                "The request/reply bridge is shutting down.")).ConfigureAwait(false);
        }
    }

    private async Task OnResponseAsync(FlowMessage<TResponse> message)
    {
        if (!_inFlight.TryRemove(message.CorrelationId, out var entry))
        {
            // No matching request: late/duplicate response or already timed out.
            EmitError(RequestReplyErrorCodes.Unmatched,
                "Received a response with no matching in-flight request.", message.CorrelationId);
            EmitEvent(RequestReplyEvents.Unmatched, message.CorrelationId, FlowEventLevel.Warning);
            return;
        }

        try
        {
            await entry.Context.ReplyAsync(message.Payload, _stopping.Token).ConfigureAwait(false);
            EmitEvent(RequestReplyEvents.Replied, message.CorrelationId);
        }
        catch (Exception exception)
        {
            EmitError(RequestReplyErrorCodes.ReplyFailed,
                $"Failed to reply: {exception.Message}", message.CorrelationId, exception);
        }
    }

    private void Sweep()
    {
        var now = _clock.GetUtcNow();
        foreach (var pair in _inFlight)
        {
            if (pair.Value.Deadline <= now && _inFlight.TryRemove(pair.Key, out var entry))
            {
                _ = TimeOutAsync(pair.Key, entry);
            }
        }
    }

    private async Task TimeOutAsync(CorrelationId id, InFlight entry)
    {
        await SafeFailAsync(entry.Context, new TimeoutException(
            $"No response within {_options.Timeout.TotalMilliseconds:0} ms.")).ConfigureAwait(false);
        EmitError(RequestReplyErrorCodes.TimedOut,
            $"Request '{id}' timed out after {_options.Timeout.TotalMilliseconds:0} ms.", id);
        EmitEvent(RequestReplyEvents.TimedOut, id, FlowEventLevel.Warning);
    }

    private static async Task SafeFailAsync(
        IRequestContext<TRequest, TResponse> context,
        Exception error)
    {
        try
        {
            await context.FailAsync(error).ConfigureAwait(false);
        }
        catch
        {
            // The caller may already be gone (aborted); failing to fail is not fatal.
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

        _sweep.Dispose();
        _incoming.Complete();
        _stopping.Cancel();

        // Fail anything still in flight so no caller hangs forever.
        foreach (var pair in _inFlight)
        {
            if (_inFlight.TryRemove(pair.Key, out var entry))
            {
                await SafeFailAsync(entry.Context, new OperationCanceledException(
                    "The request/reply bridge was disposed.")).ConfigureAwait(false);
            }
        }

        _output.Complete();
        _errors.Complete();
        _events.Complete();
        _stopping.Dispose();
    }

    private sealed record InFlight(IRequestContext<TRequest, TResponse> Context, DateTimeOffset Deadline);
}
