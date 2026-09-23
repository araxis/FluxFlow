using System.Text.Json;
using System.Threading.RateLimiting;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Coordination;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;

namespace FluxFlow.Components.RateLimit;

/// <summary>Holds a native rate-limit lease across a correlated request/reply operation.</summary>
public sealed class RateLimit : FlowTransformNode
{
    private readonly RateLimiter _limiter;
    private readonly FlowRequestReply _exchanges = new();
    private readonly FlowOutput<FlowMessage> _requests;
    private readonly TimeSpan _timeout;

    public ISourceBlock<FlowMessage> Request => _requests;
    public ITargetBlock<FlowMessage> Reply { get; }

    public RateLimit(RateLimitOptions options)
        : base(Validate(options).Execution ?? new ExecutionDataflowBlockOptions
        {
            BoundedCapacity = 128, MaxDegreeOfParallelism = DataflowBlockOptions.Unbounded
        }, options.OutputCapacity)
    {
        _limiter = options.Limiter;
        _timeout = options.ResponseTimeout;
        _requests = AddOutput<FlowMessage>();
        Reply = AddControlInput(nameof(Reply), (message, _) =>
        {
            _exchanges.Reply(nameof(Reply), message);
            return Task.CompletedTask;
        }, options.OutputCapacity);
    }

    protected override async Task<IEnumerable<FlowMessage>> TransformAsync(FlowMessage message)
    {
        using var lease = await _limiter.AcquireAsync(1, Stopping).ConfigureAwait(false);
        if (!lease.IsAcquired)
        {
            JsonElement? details = lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
                ? JsonSerializer.SerializeToElement(new Dictionary<string, double> { ["retryAfterSeconds"] = retryAfter.TotalSeconds })
                : null;
            return [message.WithValue(new FlowError("rate_limit_rejected", "The rate limiter rejected the request.",
                "rate_limit", isTransient: true, details: details))];
        }

        var reply = await _exchanges.RequestAsync(nameof(Reply), message.WithValue(message.Value),
            (request, token) => EmitAsync(_requests, request, token), _timeout, Stopping).ConfigureAwait(false);
        return [message.WithValue(reply.Value, reply.Headers, reply.MessageId)];
    }

    protected override ValueTask OnDisposeAsync() => _exchanges.DisposeAsync();

    private static RateLimitOptions Validate(RateLimitOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Limiter);
        if (options.ResponseTimeout != Timeout.InfiniteTimeSpan &&
            (options.ResponseTimeout <= TimeSpan.Zero || options.ResponseTimeout.TotalMilliseconds > uint.MaxValue - 1))
            throw new ArgumentOutOfRangeException(nameof(options.ResponseTimeout));
        return options;
    }
}
