using FluxFlow.Core.Messaging;
using FluxFlow.Core.Coordination;
using FluxFlow.Core.Nodes;
using System.Threading.Tasks.Dataflow;
using Polly;

namespace FluxFlow.Components.Resilience;

/// <summary>Retries independent request/reply attempts and publishes only the final result.</summary>
public sealed class Retry : FlowTransformNode
{
    private readonly FlowRequestReply _exchanges = new();
    private readonly FlowOutput<FlowMessage> _requests;
    private readonly TimeSpan _timeout;
    private readonly ResiliencePipeline<FlowMessage> _pipeline;

    public ISourceBlock<FlowMessage> Request => _requests;
    public ITargetBlock<FlowMessage> Reply { get; }

    public Retry(RetryOptions options) : this(options, BuildPipeline(options)) { }

    private Retry(RetryOptions options, ResiliencePipeline<FlowMessage> pipeline)
        : base(options.Execution, options.OutputCapacity)
    {
        (_timeout, _pipeline) = (options.AttemptTimeout, pipeline);
        _requests = AddOutput<FlowMessage>();
        Reply = AddControlInput(nameof(Reply), (message, _) =>
        {
            _exchanges.Reply(nameof(Reply), message);
            return Task.CompletedTask;
        }, options.OutputCapacity);
    }

    protected override async Task<IEnumerable<FlowMessage>> TransformAsync(FlowMessage message)
    {
        var reply = await _pipeline.ExecuteAsync(async cancellationToken =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var attempt = message.WithValue(message.Value);
            return await _exchanges.RequestAsync(nameof(Reply), attempt,
                (request, token) => EmitAsync(_requests, request, token),
                _timeout, cancellationToken).ConfigureAwait(false);
        }, Stopping).ConfigureAwait(false);
        // Restore the outer exchange while retaining the final reply's payload and headers.
        return [message.WithValue(reply.Value, reply.Headers, reply.MessageId)];
    }

    protected override ValueTask OnDisposeAsync() => _exchanges.DisposeAsync();

    private static ResiliencePipeline<FlowMessage> BuildPipeline(RetryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.RetryPolicy);
        if (options.AttemptTimeout != Timeout.InfiniteTimeSpan &&
            (options.AttemptTimeout <= TimeSpan.Zero || options.AttemptTimeout.TotalMilliseconds > uint.MaxValue - 1))
            throw new ArgumentOutOfRangeException(nameof(options.AttemptTimeout));
        return new ResiliencePipelineBuilder<FlowMessage>().AddRetry(options.RetryPolicy).Build();
    }
}
