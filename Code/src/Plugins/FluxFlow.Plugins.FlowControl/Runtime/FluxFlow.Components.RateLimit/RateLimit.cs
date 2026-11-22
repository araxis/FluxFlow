using System.Diagnostics;
using System.Text.Json;
using System.Threading.RateLimiting;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Coordination;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;

namespace FluxFlow.Components.RateLimit;
/// <summary>Holds a native rate-limit lease across a correlated request/reply operation.</summary>
public sealed class RateLimit : FlowNode
{
    public ITargetBlock<FlowMessage> Input { get; }

    private readonly RateLimiter _limiter;
    private readonly FlowRequestReply _exchanges;
    private readonly BroadcastBlock<FlowMessage> _requests;
    private readonly TimeSpan _timeout;
    private readonly bool _ownsLimiter;
    private readonly bool _stopDispatchOnTimeout;
    private readonly bool _emitResults;
    private readonly object _dispatchGate = new();
    private bool _dispatchStopped;
    public ISourceBlock<FlowMessage> Request => _requests;
    public ITargetBlock<FlowMessage> Reply { get; }
    public ITargetBlock<FlowMessage> Nack { get; }

    public RateLimit(RateLimitOptions options)
    {
        Validate(options);
        _limiter = options.Limiter;
        _timeout = options.ResponseTimeout;
        _ownsLimiter = options.OwnsLimiter;
        _stopDispatchOnTimeout = options.StopDispatchOnTimeout;
        _emitResults = options.EmitRequestReplyResults;
        var execution = options.Execution ?? new ExecutionDataflowBlockOptions
        {
            BoundedCapacity = 128,
            MaxDegreeOfParallelism = DataflowBlockOptions.Unbounded
        };
        var outputOptions = new DataflowBlockOptions { BoundedCapacity = options.OutputCapacity };
        Output = new BroadcastBlock<FlowMessage>(static message => message, outputOptions);
        Events = new BroadcastBlock<FlowMessage>(static message => message);
        Lifetime = new FlowLifetime(this, cancellationToken: execution.CancellationToken);
        var input = new TransformManyBlock<FlowMessage, FlowMessage>(message => message.ProcessMessageAsync(this, TransformAsync, Output, Stopping, handlesErrors: _emitResults), execution);
        input.LinkTo(Output);
        Input = input;
        _requests = new BroadcastBlock<FlowMessage>(static message => message, outputOptions);
        _exchanges = new FlowRequestReply(options.PendingCapacity);
        Reply = new ActionBlock<FlowMessage>(controlMessage => FlowBlockExtensions.ProcessControlAsync(controlMessage, this, nameof(Reply), (message, _) =>
        {
            _exchanges.Reply(nameof(Reply), _emitResults
                ? message.WithValue(new Feedback(RequestReplyOutcome.ACK, message)) : message);
        }, Stopping, Fault), new ExecutionDataflowBlockOptions { BoundedCapacity = options.OutputCapacity });
        Nack = new ActionBlock<FlowMessage>(controlMessage => FlowBlockExtensions.ProcessControlAsync(controlMessage, this, nameof(Nack), (message, _) =>
        {
            var rejected = _emitResults ? message.WithValue(new Feedback(RequestReplyOutcome.NACK, message))
                : message.IsError ? message : message.WithValue(new FlowError(
                "request_reply_rejected", "The request was rejected by the processing path.", "request_reply", details: message.Value));
            _exchanges.Reply(nameof(Reply), rejected);
        }, Stopping, Fault), new ExecutionDataflowBlockOptions { BoundedCapacity = options.OutputCapacity });
        Lifetime.HasInput(input);
        Lifetime.HasOutput(Output);
        Lifetime.HasOutput(_requests);
        Lifetime.HasTerminalInput(Reply);
        Lifetime.HasTerminalInput(Nack);
        Lifetime.HasEvents(Events);
        Lifetime.Observe();
    }

    private async Task<IEnumerable<FlowMessage>> TransformAsync(FlowMessage message)
    {
        using var lease = await _limiter.AcquireAsync(1, Stopping).ConfigureAwait(false);
        if (!lease.IsAcquired)
        {
            FlowValue? details = lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
                ? new JsonFlowValue(JsonSerializer.SerializeToElement(new { retryAfterSeconds = retryAfter.TotalSeconds }))
                : null;
            return[message.WithValue(new FlowError("rate_limit_rejected", "The rate limiter rejected the request.", "rate_limit", isTransient: true, details: details))];
        }

        Task<FlowMessage> exchange;
        var started = Stopwatch.GetTimestamp();
        lock (_dispatchGate)
        {
            if (_dispatchStopped)
                return [message.WithValue(_emitResults
                    ? new RequestReplyResult(RequestReplyOutcome.NotDispatched, message, null, TimeSpan.Zero)
                    : new FlowError("request_reply_dispatch_stopped",
                        "This request was not dispatched because an earlier request timed out. Stop and rerun to resume.", "request_reply"))];
            exchange = _exchanges.RequestAsync(nameof(Reply), message.WithValue(message.Value),
                (request, token) => EmitAsync(_requests, request, token), _timeout, Stopping);
        }
        try
        {
            var reply = await exchange.ConfigureAwait(false);
            if (_emitResults)
            {
                var feedback = (Feedback)reply.Value;
                return [message.WithValue(new RequestReplyResult(feedback.Outcome, message, feedback.Message.Value,
                    Stopwatch.GetElapsedTime(started)), feedback.Message.Headers, feedback.Message.MessageId)];
            }
            return[message.WithValue(reply.Value, reply.Headers, reply.MessageId)];
        }
        catch (TimeoutException)
        {
            if (_stopDispatchOnTimeout)
                lock (_dispatchGate) _dispatchStopped = true;
            if (_emitResults)
                return [message.WithValue(new RequestReplyResult(RequestReplyOutcome.Timeout, message, null,
                    Stopwatch.GetElapsedTime(started)))];
            throw;
        }
    }

    protected override async ValueTask OnDisposeAsync()
    {
        try { await _exchanges.DisposeAsync().ConfigureAwait(false); }
        finally { if (_ownsLimiter) await _limiter.DisposeAsync().ConfigureAwait(false); }
    }
    private static void Validate(RateLimitOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Limiter);
        if (options.ResponseTimeout != Timeout.InfiniteTimeSpan && (options.ResponseTimeout <= TimeSpan.Zero || options.ResponseTimeout.TotalMilliseconds > uint.MaxValue - 1))
            throw new ArgumentOutOfRangeException(nameof(options.ResponseTimeout));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.OutputCapacity, nameof(options.OutputCapacity));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.PendingCapacity, nameof(options.PendingCapacity));
    }

    private sealed class Feedback(RequestReplyOutcome outcome, FlowMessage message) : FlowValue
    {
        public RequestReplyOutcome Outcome { get; } = outcome;
        public FlowMessage Message { get; } = message;
        protected override JsonElement CreateJson() => Message.Value.Json;
    }
}
