using FluxFlow.Engine.Components;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Engine.Runtime;

internal sealed class FlowErrorCollector : IDisposable, IAsyncDisposable
{
    private readonly FlowFanoutSource<RuntimeFlowError> _errors = new();
    private readonly IReadOnlyList<Subscription> _subscriptions;
    private int _completed;
    private int _disposeStarted;

    public FlowErrorCollector(IEnumerable<RuntimeNode> nodes)
    {
        _subscriptions = nodes
            .Select(CreateSubscription)
            .ToArray();
    }

    public ISourceBlock<RuntimeFlowError> Errors => _errors;

    public bool Post(RuntimeFlowError error)
        => _errors.Post(error);

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
                    _errors.Fault(task.Exception?.InnerException ?? task.Exception!);
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
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0)
        {
            return;
        }

        StopSubscriptions();
        try
        {
            Task.WhenAll(_subscriptions.Select(subscription => subscription.Target.Completion))
                .GetAwaiter()
                .GetResult();
        }
        finally
        {
            Complete();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0)
        {
            return;
        }

        StopSubscriptions();
        try
        {
            await Task.WhenAll(_subscriptions.Select(subscription => subscription.Target.Completion))
                .ConfigureAwait(false);
        }
        finally
        {
            Complete();
        }
    }

    private void StopSubscriptions()
    {
        foreach (var subscription in _subscriptions)
        {
            subscription.Link.Dispose();
            subscription.Target.Complete();
        }
    }

    private Subscription CreateSubscription(RuntimeNode node)
    {
        var target = new ActionBlock<FlowError>(
            error => _errors.Post(Enrich(node, error)));
        var link = node.Node.Errors.LinkTo(
            target,
            new DataflowLinkOptions { PropagateCompletion = true });

        return new Subscription(link, target);
    }

    private static RuntimeFlowError Enrich(
        RuntimeNode node,
        FlowError error)
        => new()
        {
            NodeAddress = node.Address,
            NodeId = node.Node.Id,
            NodeType = node.Type,
            NodePhase = node.Phase,
            Error = error
        };

    private void Complete()
    {
        if (Interlocked.Exchange(ref _completed, 1) == 0)
        {
            _errors.Complete();
        }
    }

    private sealed record Subscription(
        IDisposable Link,
        ITargetBlock<FlowError> Target);
}
