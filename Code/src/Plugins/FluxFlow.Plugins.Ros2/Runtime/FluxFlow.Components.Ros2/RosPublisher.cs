using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;
using Rcl;
using Rosidl.Runtime;

namespace FluxFlow.Components.Ros2;

/// <summary>Publishes values to ROS and forwards the original envelope after local publication succeeds.</summary>
public sealed class RosPublisher<T> : FlowNode where T : class, IMessage
{
    private readonly IRclPublisher<T> _publisher;
    private readonly Func<FlowValue, T> _convert;
    public ITargetBlock<FlowMessage> Input { get; }

    public RosPublisher(RosPublisherOptions<T> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Node);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Topic);
        ArgumentNullException.ThrowIfNull(options.Publisher);
        ArgumentNullException.ThrowIfNull(options.ToMessage);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.OutputCapacity);
        _convert = options.ToMessage;
        var execution = options.Execution ?? new ExecutionDataflowBlockOptions { BoundedCapacity = 128 };
        Output = new BroadcastBlock<FlowMessage>(static message => message,
            new DataflowBlockOptions { BoundedCapacity = options.OutputCapacity });
        Events = new BroadcastBlock<FlowMessage>(static message => message);
        Lifetime = new FlowLifetime(this, cancellationToken: execution.CancellationToken);
        var input = new TransformManyBlock<FlowMessage, FlowMessage>(
            message => message.ProcessMessageAsync(this, PublishAsync, Output, Stopping), execution);
        input.LinkTo(Output);
        Input = input;
        Lifetime.HasInput(input);
        Lifetime.HasOutput(Output);
        Lifetime.HasEvents(Events);
        _publisher = options.Node.CreatePublisher<T>(options.Topic, options.Publisher);
        try { Lifetime.Observe(); }
        catch { _publisher.Dispose(); throw; }
    }

    private async Task<IEnumerable<FlowMessage>> PublishAsync(FlowMessage message)
    {
        Stopping.ThrowIfCancellationRequested();
        var value = _convert(message.Value) ?? throw new InvalidOperationException("ToMessage returned null.");
        // Rcl.NET has no cancellation token for an in-flight native publish. Await it before disposal.
        await _publisher.PublishAsync(value).ConfigureAwait(false);
        return [message];
    }

    protected override ValueTask OnDisposeAsync()
    {
        _publisher.Dispose();
        return ValueTask.CompletedTask;
    }
}
