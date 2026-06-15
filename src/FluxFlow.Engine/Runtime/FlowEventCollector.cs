using FluxFlow.Engine.Components;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Engine.Runtime;

internal sealed class FlowEventCollector : IDisposable
{
    private readonly FlowFanoutSource<FlowEvent> _events = new();
    private readonly IReadOnlyList<Subscription> _subscriptions;
    private int _completed;

    public FlowEventCollector(IEnumerable<RuntimeNode> nodes)
    {
        _subscriptions = nodes
            .Select(node => node.Node)
            .OfType<IFlowEventSource>()
            .Select(CreateSubscription)
            .ToArray();
    }

    public ISourceBlock<FlowEvent> Events => _events;

    public void CompleteWhen(Task runtimeCompletion)
    {
        ArgumentNullException.ThrowIfNull(runtimeCompletion);

        var completions = _subscriptions
            .Select(subscription => subscription.Target.Completion)
            .Append(runtimeCompletion);

        _ = Task.WhenAll(completions).ContinueWith(
            task =>
            {
                if (task.IsFaulted)
                {
                    _events.Fault(task.Exception?.InnerException ?? task.Exception!);
                    return;
                }

                Complete();
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    public void Dispose()
    {
        foreach (var subscription in _subscriptions)
        {
            subscription.Link.Dispose();
            subscription.Target.Complete();
        }

        Complete();
    }

    private Subscription CreateSubscription(IFlowEventSource source)
    {
        var target = new ActionBlock<FlowEvent>(flowEvent => _events.Post(flowEvent));
        var link = source.Events.LinkTo(
            target,
            new DataflowLinkOptions { PropagateCompletion = true });

        return new Subscription(link, target);
    }

    private void Complete()
    {
        if (Interlocked.Exchange(ref _completed, 1) == 0)
        {
            _events.Complete();
        }
    }

    private sealed record Subscription(
        IDisposable Link,
        ITargetBlock<FlowEvent> Target);
}
