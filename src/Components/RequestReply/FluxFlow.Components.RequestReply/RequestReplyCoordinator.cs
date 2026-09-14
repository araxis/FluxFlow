using FluxFlow.Core;
using FluxFlow.Data;
using FluxFlow.Nodes;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.RequestReply;

/// <summary>
/// Adapts typed transport contexts to canonical workflow exchanges. Core owns
/// exchange identity, deadlines and matching; this bridge owns transport callbacks.
/// Configure links before startup. Production drain leaves response controls open.
/// </summary>
public sealed class RequestReplyCoordinator<TRequest, TResponse> : IFlowSource, IFlowComponent
{
    private const string ReplyRoute = "Responses";
    private readonly RequestReplyOptions _options;
    private readonly TimeProvider _clock;
    private readonly FlowRequestReply? _exchanges;
    private readonly ActionBlock<IRequestContext<TRequest, TResponse>> _incoming;
    private readonly ActionBlock<FlowMessage> _responses;
    private readonly FlowOutput<FlowMessage> _output;
    private readonly BroadcastBlock<FlowMessage> _events = new(static value => value);
    private readonly Func<FlowMessage, FlowMessage<TResponse>> _materializeResponse;
    private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _stopping = new();
    private readonly object _shutdownGate = new();
    private readonly HashSet<Task> _callers = [];
    private readonly Task _productionCompletion;
    private readonly Task _completion;
    private Task? _shutdownTask;
    private Exception? _shutdownReason;
    private Exception? _cancellationFailure;
    private int _disposed;

    /// <summary>Creates a stopped bridge. The materializer converts only the caller result, not exchange identity.</summary>
    public RequestReplyCoordinator(RequestReplyOptions? options = null, TimeProvider? clock = null,
        Func<FlowMessage, FlowMessage<TResponse>>? materializeResponse = null)
    {
        _options = options ?? new RequestReplyOptions();
        _clock = clock ?? TimeProvider.System;
        _materializeResponse = materializeResponse ?? MaterializeResponse;
        _exchanges = _options.Mode == RequestReplyMode.RequestReply
            ? new FlowRequestReply(_options.Capacity, _clock) : null;
        _output = new FlowOutput<FlowMessage>(new FlowOutputOptions { Capacity = _options.Capacity });
        _incoming = new ActionBlock<IRequestContext<TRequest, TResponse>>(OnIncomingAsync,
            new ExecutionDataflowBlockOptions { BoundedCapacity = _options.Capacity, MaxDegreeOfParallelism = 1 });
        _responses = new ActionBlock<FlowMessage>(OnResponse,
            new ExecutionDataflowBlockOptions { BoundedCapacity = _options.Capacity, MaxDegreeOfParallelism = 1 });
        Ports = new FlowPorts(
            outputs: new Dictionary<string, ISourceBlock<FlowMessage>> { ["Output"] = Output },
            events: Events,
            controls: new Dictionary<string, ITargetBlock<FlowMessage>> { [ReplyRoute] = Responses });
        _productionCompletion = FinishInputAsync();
        _completion = FinishAsync();
    }

    public ITargetBlock<IRequestContext<TRequest, TResponse>> Incoming => _incoming;
    public ISourceBlock<FlowMessage> Output => _output;
    public ITargetBlock<FlowMessage> Responses => _responses;
    public ISourceBlock<FlowMessage> Events => _events;
    public int InFlightCount => _exchanges?.PendingCount ?? 0;
    public FlowPorts Ports { get; }
    public Task Completion => _completion;
    public Task ProductionCompletion => _productionCompletion;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested) return Task.FromCanceled(cancellationToken);
        _started.TrySetResult();
        return Task.CompletedTask;
    }

    public void StopProduction()
    {
        _incoming.Complete();
        _started.TrySetCanceled();
    }

    public void Complete()
    {
        if (Volatile.Read(ref _disposed) == 0)
            BeginShutdown(new OperationCanceledException("The request/reply bridge was completed."));
    }

    public void Fault(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (Volatile.Read(ref _disposed) == 0) BeginShutdown(exception, fault: true);
    }

    private async Task OnIncomingAsync(IRequestContext<TRequest, TResponse> context)
    {
        using var suppressed = FlowTracing.SuppressContext();
        var notificationStarted = false;
        try
        {
            if (context is null)
            {
                EmitError(RequestReplyErrorCodes.InvalidRequestContext, "Request context is required.", null);
                EmitEvent(RequestReplyEvents.Invalid, null, FlowEventLevel.Error);
                return;
            }
            await _started.Task.WaitAsync(_stopping.Token).ConfigureAwait(false);
            _stopping.Token.ThrowIfCancellationRequested();
            if (_exchanges is null)
            {
                var message = CreateMessage(context, context.CorrelationId ?? CorrelationId.New());
                EmitEvent(RequestReplyEvents.Received, message.CorrelationId);
                await PublishAsync(message, _stopping.Token).ConfigureAwait(false);
                notificationStarted = true;
                await SafeAcknowledgeAsync(context).ConfigureAwait(false);
                return;
            }

            var published = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Exception? rejection;
            lock (_shutdownGate)
            {
                rejection = _shutdownReason;
                if (rejection is null && _callers.Count >= _options.Capacity)
                    rejection = new InvalidOperationException($"The request/reply bridge has reached its {_options.Capacity} caller limit.");
                if (rejection is null)
                {
                    var caller = HandleRequestAsync(context, published);
                    _callers.Add(caller);
                    _ = caller.ContinueWith(completed =>
                    {
                        lock (_shutdownGate) _callers.Remove(completed);
                        _ = completed.Exception;
                    }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                }
            }
            if (rejection is not null)
            {
                notificationStarted = true;
                await SafeFailAsync(context, rejection).ConfigureAwait(false);
                if (_shutdownReason is null)
                    EmitError(RequestReplyErrorCodes.CapacityReached, rejection.Message, context.CorrelationId);
                return;
            }
            // Do not wait for the reply here: output must finish before response controls close.
            await published.Task.ConfigureAwait(false);
        }
        catch (OperationCanceledException error) when (!notificationStarted)
        {
            if (context is not null) await SafeFailAsync(context, _shutdownReason ?? error).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            Fault(error);
            if (!notificationStarted && context is not null)
                await SafeFailAsync(context, _shutdownReason ?? error).ConfigureAwait(false);
        }
    }

    private async Task HandleRequestAsync(IRequestContext<TRequest, TResponse> context, TaskCompletionSource published)
    {
        // Register ownership before invoking any transport code.
        await Task.Yield();
        CorrelationId? id = null;
        var notificationStarted = false;
        try
        {
            var request = CreateMessage(context, context.CorrelationId);
            FlowMessage response;
            try
            {
                response = await _exchanges!.RequestAsync(ReplyRoute, request, async (message, token) =>
                {
                    id = message.CorrelationId;
                    EmitEvent(RequestReplyEvents.Received, id);
                    try { await PublishAsync(message, token).ConfigureAwait(false); }
                    finally { published.TrySetResult(); }
                }, _options.Timeout, _stopping.Token).ConfigureAwait(false);
            }
            catch (TimeoutException error)
            {
                notificationStarted = true;
                await SafeFailAsync(context, error).ConfigureAwait(false);
                EmitError(RequestReplyErrorCodes.TimedOut, error.Message, id, error);
                EmitEvent(RequestReplyEvents.TimedOut, id, FlowEventLevel.Warning);
                return;
            }
            using var activity = FlowTracing.StartProcessing(response, this, "Responses");
            var result = _materializeResponse(response);
            notificationStarted = true;
            if (result.IsError)
                await SafeFailAsync(context, new RequestReplyResponseException(result.Error!), _stopping.Token).ConfigureAwait(false);
            else
            {
                try { await context.ReplyAsync(result.Value, _stopping.Token).ConfigureAwait(false); }
                catch (Exception error)
                {
                    FlowDiagnostics.ProcessingException(response, this, error);
                    EmitError(RequestReplyErrorCodes.ReplyFailed, $"Failed to reply: {error.Message}", id, error);
                    return;
                }
            }
            EmitEvent(RequestReplyEvents.Replied, id, result.IsError ? FlowEventLevel.Warning : FlowEventLevel.Information);
        }
        catch (OperationCanceledException error) when (!notificationStarted)
        {
            await SafeFailAsync(context, _shutdownReason ?? error).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            Fault(error);
            if (!notificationStarted) await SafeFailAsync(context, _shutdownReason ?? error).ConfigureAwait(false);
        }
        finally { published.TrySetResult(); }
    }

    private static FlowMessage CreateMessage(IRequestContext<TRequest, TResponse> context, CorrelationId? correlationId)
    {
        var trace = context.TraceContext;
        Dictionary<string, string>? headers = null;
        if (trace.TraceId != default && trace.SpanId != default)
        {
            headers = new Dictionary<string, string>
            {
                ["traceparent"] = $"00-{trace.TraceId}-{trace.SpanId}-{(byte)(trace.TraceFlags & ActivityTraceFlags.Recorded):x2}"
            };
            if (!string.IsNullOrEmpty(trace.TraceState)) headers["tracestate"] = trace.TraceState;
        }
        return FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(context.Request, ValueJsonOptions)), correlationId, headers: headers);
    }

    private async ValueTask PublishAsync(FlowMessage message, CancellationToken cancellationToken)
    {
        if (!await _output.SendAsync(message, cancellationToken).ConfigureAwait(false))
            throw _shutdownReason ?? new InvalidOperationException("The request output rejected admission.");
        EmitEvent(RequestReplyEvents.Published, message.CorrelationId);
    }

    private void OnResponse(FlowMessage message)
    {
        if (message is null)
        {
            EmitError(RequestReplyErrorCodes.InvalidResponseMessage, "Response message is required.", null);
            EmitEvent(RequestReplyEvents.Invalid, null, FlowEventLevel.Error);
            return;
        }
        if (_exchanges?.Reply(ReplyRoute, message) != true)
        {
            EmitError(RequestReplyErrorCodes.Unmatched,
                "Received a response with no matching in-flight request.", message.CorrelationId);
            EmitEvent(RequestReplyEvents.Unmatched, message.CorrelationId, FlowEventLevel.Warning);
        }
    }

    private FlowMessage<TResponse> MaterializeResponse(FlowMessage message)
    {
        if (message.IsError) return message.ConvertError<TResponse>();
        try { return message.ConvertValue((message.Value is TResponse typedInput0 ? typedInput0 : typeof(TResponse) == typeof(JsonElement) ? (TResponse)(object)message.Value.Json : message.Value.Json.Deserialize<TResponse>(ValueJsonOptions))!); }
        catch (Exception error) when (error is JsonException or InvalidOperationException or NotSupportedException)
        {
            FlowDiagnostics.ProcessingException(message, this, error);
            return message.WithError(new FlowError(
                "request.reply.invalid_shape", error.Message, "RequestReply")).ConvertError<TResponse>();
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

    private void EmitEvent(string name, CorrelationId? id, FlowEventLevel level = FlowEventLevel.Information)
        => _events.Post(new FlowEvent
        {
            Timestamp = _clock.GetUtcNow(),
            CorrelationId = id,
            Name = name,
            Level = level
        });

    private void EmitError(int code, string message, CorrelationId? id, Exception? exception = null)
        => _events.Post(new FlowEvent
        {
            Timestamp = _clock.GetUtcNow(),
            CorrelationId = id,
            Name = RequestReplyEvents.Invalid,
            Level = FlowEventLevel.Error,
            Message = message,
            Attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["code"] = code,
                ["exceptionType"] = exception?.GetType().FullName
            }
        });

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        BeginShutdown(new OperationCanceledException("The request/reply bridge was disposed."));
        try { await Completion.ConfigureAwait(false); }
        catch { /* Completion retains the processing fault. */ }
        var failures = new List<Exception>();
        try { await _shutdownTask!.ConfigureAwait(false); }
        catch (Exception error) { AddFailure(failures, error); }
        lock (_shutdownGate)
            if (_cancellationFailure is not null) AddFailure(failures, _cancellationFailure);
        try { await _output.DisposeAsync().ConfigureAwait(false); }
        catch (Exception error) { AddFailure(failures, error); }
        finally { _stopping.Dispose(); }
        ThrowFailures(failures);
    }

    private void BeginShutdown(Exception reason, bool fault = false)
    {
        lock (_shutdownGate)
        {
            _shutdownReason ??= reason;
            // Preserve the initiating fault before cleanup/cancellation can fault again.
            if (fault)
            {
                _output.Fault(reason);
                ((IDataflowBlock)_responses).Fault(reason);
            }
            _shutdownTask ??= SettleCallersAsync(_shutdownReason);
        }
        try { _stopping.Cancel(); }
        catch (Exception error) { lock (_shutdownGate) _cancellationFailure ??= error; }
        _incoming.Complete(); // Process queued contexts to their terminal callback; never discard them.
        if (!fault)
        {
            _responses.Complete();
            _output.Complete();
        }
    }

    private async Task SettleCallersAsync(Exception reason)
    {
        await Task.Yield();
        var failures = new List<Exception>();
        try { _exchanges?.Stop(reason); }
        catch (Exception error) { AddFailure(failures, error); }
        try { await _incoming.Completion.ConfigureAwait(false); }
        catch (Exception error) { AddFailure(failures, error); }
        Task[] callers;
        lock (_shutdownGate) callers = _callers.ToArray();
        try { await Task.WhenAll(callers).ConfigureAwait(false); }
        catch (Exception error) { AddFailure(failures, error); }
        try { if (_exchanges is not null) await _exchanges.DisposeAsync().ConfigureAwait(false); }
        catch (Exception error) { AddFailure(failures, error); }
        ThrowFailures(failures);
    }

    private async Task FinishAsync()
    {
        var failures = new List<Exception>();
        var blocks = Task.WhenAll(_productionCompletion,
            ObserveFaultAsync(_responses.Completion), ObserveFaultAsync(_output.Completion));
        try { await blocks.ConfigureAwait(false); }
        catch (Exception error) { AddFailure(failures, blocks.Exception ?? error); }
        BeginShutdown(new OperationCanceledException("The request/reply bridge stopped."));
        try { await _shutdownTask!.ConfigureAwait(false); }
        catch (Exception error) { AddFailure(failures, error); }
        lock (_shutdownGate)
            if (_cancellationFailure is not null) AddFailure(failures, _cancellationFailure);
        _events.Complete();
        await _events.Completion.ConfigureAwait(false);
        foreach (var failure in failures)
            FlowDiagnostics.LifecycleException(this, "request.reply", failure);
        ThrowFailures(failures);
    }

    private async Task FinishInputAsync()
    {
        try { await ObserveFaultAsync(_incoming.Completion).ConfigureAwait(false); }
        finally { _output.Complete(); }
        await ObserveFaultAsync(_output.Completion).ConfigureAwait(false);
    }

    private async Task ObserveFaultAsync(Task completion)
    {
        try { await completion.ConfigureAwait(false); }
        catch (Exception exception) { Fault(exception); throw; }
    }

    private static void AddFailure(List<Exception> failures, Exception exception)
    {
        if (exception is AggregateException aggregate)
        {
            foreach (var error in aggregate.InnerExceptions) AddFailure(failures, error);
            return;
        }
        if (!failures.Contains(exception)) failures.Add(exception);
    }

    private static void ThrowFailures(List<Exception> failures)
    {
        if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1) throw new AggregateException(failures);
    }


    private sealed class RequestReplyResponseException(FlowError error)
        : Exception($"{error.Code}: {error.Message}");
    private static readonly JsonSerializerOptions ValueJsonOptions = new(JsonSerializerDefaults.Web);
}
