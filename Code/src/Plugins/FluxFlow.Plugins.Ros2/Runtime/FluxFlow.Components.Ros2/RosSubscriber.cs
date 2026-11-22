using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;
using Rcl;
using Rosidl.Runtime;

namespace FluxFlow.Components.Ros2;

/// <summary>Starts a native ROS subscription when the source starts and emits a new envelope per received message.</summary>
public sealed class RosSubscriber<T> : FlowNode, IFlowSource where T : class, IMessage
{
    private readonly IRclNode _node;
    private readonly string _topic;
    private readonly SubscriptionOptions _subscription;
    private readonly Func<T, FlowValue> _convert;

    public RosSubscriber(RosSubscriberOptions<T> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Node);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Topic);
        ArgumentNullException.ThrowIfNull(options.Subscription);
        ArgumentNullException.ThrowIfNull(options.ToValue);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.OutputCapacity);
        (_node, _topic, _subscription, _convert) = (options.Node, options.Topic, options.Subscription, options.ToValue);
        Output = new BroadcastBlock<FlowMessage>(static message => message,
            new DataflowBlockOptions { BoundedCapacity = options.OutputCapacity });
        Events = new BroadcastBlock<FlowMessage>(static message => message);
        Lifetime = new FlowLifetime(this, produce: RunAsync);
        Lifetime.HasOutput(Output);
        Lifetime.HasEvents(Events);
        Lifetime.Observe();
    }

    public Task ProductionCompletion => Lifetime.ProductionCompletion;
    public Task StartAsync(CancellationToken cancellationToken = default) => Lifetime.StartAsync(cancellationToken);
    public void StopProduction() => Lifetime.StopProduction();

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var subscription = _node.CreateSubscription<T>(_topic, _subscription);
        await foreach (var value in subscription.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            FlowMessage message;
            try
            {
                message = FlowMessage.Create(_convert(value)
                    ?? throw new InvalidOperationException("ToValue returned null."));
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                message = FlowMessage.Create(new FlowError("ros.conversion_failed", error.Message, "ros"));
            }
            await EmitAsync(message, cancellationToken).ConfigureAwait(false);
        }
    }
}
