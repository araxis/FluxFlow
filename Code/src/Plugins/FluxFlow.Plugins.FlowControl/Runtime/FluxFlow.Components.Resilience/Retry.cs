using FluxFlow.Core.Messaging;
using FluxFlow.Core.Coordination;
using FluxFlow.Core.Nodes;
using System.Threading.Tasks.Dataflow;
using Polly;

namespace FluxFlow.Components.Resilience;
/// <summary>Retries independent request/reply attempts and publishes only the final result.</summary>
public sealed class Retry : FlowNode
{
    public ITargetBlock<FlowMessage> Input { get; }

    private readonly FlowRequestReply _exchanges;
    private readonly BroadcastBlock<FlowMessage> _requests;
    private readonly TimeSpan _timeout;
    private readonly ResiliencePipeline<FlowMessage> _pipeline;
    public ISourceBlock<FlowMessage> Request => _requests;
    public ITargetBlock<FlowMessage> Reply { get; }

    public Retry(RetryOptions options)
    {
        Validate(options);
        _timeout = options.AttemptTimeout;
        _pipeline = new ResiliencePipelineBuilder<FlowMessage>().AddRetry(options.RetryPolicy).Build();
        var execution = options.Execution ?? new ExecutionDataflowBlockOptions { BoundedCapacity = 128 };
        var outputOptions = new DataflowBlockOptions { BoundedCapacity = options.OutputCapacity };
        Output = new BroadcastBlock<FlowMessage>(static message => message, outputOptions);
        Events = new BroadcastBlock<FlowMessage>(static message => message);
        Lifetime = new FlowLifetime(this, cancellationToken: execution.CancellationToken);
        var input = new TransformManyBlock<FlowMessage, FlowMessage>(message => message.ProcessMessageAsync(this, TransformAsync, Output, Stopping), execution);
        input.LinkTo(Output);
        Input = input;
        _requests = new BroadcastBlock<FlowMessage>(static message => message, outputOptions);
        _exchanges = new FlowRequestReply();
        Reply = new ActionBlock<FlowMessage>(controlMessage => FlowBlockExtensions.ProcessControlAsync(controlMessage, this, nameof(Reply), (message, _) =>
        {
            _exchanges.Reply(nameof(Reply), message);
        }, Stopping, Fault), new ExecutionDataflowBlockOptions { BoundedCapacity = options.OutputCapacity });
        Lifetime.HasInput(input);
        Lifetime.HasOutput(Output);
        Lifetime.HasOutput(_requests);
        Lifetime.HasTerminalInput(Reply);
        Lifetime.HasEvents(Events);
        Lifetime.Observe();
    }

    private async Task<IEnumerable<FlowMessage>> TransformAsync(FlowMessage message)
    {
        var reply = await _pipeline.ExecuteAsync(async cancellationToken =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var attempt = message.WithValue(message.Value);
            return await _exchanges.RequestAsync(nameof(Reply), attempt, (request, token) => EmitAsync(_requests, request, token), _timeout, cancellationToken).ConfigureAwait(false);
        }, Stopping).ConfigureAwait(false);
        // Restore the outer exchange while retaining the final reply's payload and headers.
        return[message.WithValue(reply.Value, reply.Headers, reply.MessageId)];
    }

    protected override ValueTask OnDisposeAsync() => _exchanges.DisposeAsync();
    private static void Validate(RetryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.RetryPolicy);
        if (options.AttemptTimeout != Timeout.InfiniteTimeSpan && (options.AttemptTimeout <= TimeSpan.Zero || options.AttemptTimeout.TotalMilliseconds > uint.MaxValue - 1))
            throw new ArgumentOutOfRangeException(nameof(options.AttemptTimeout));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.OutputCapacity, nameof(options.OutputCapacity));
    }
}
