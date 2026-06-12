using FluxFlow.Engine.Components;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Engine.Runtime;

internal sealed class FlowDiagnosticCollector : IDisposable, IAsyncDisposable
{
    private readonly FlowFanoutSource<RuntimeFlowDiagnostic> _diagnostics = new();
    private readonly IReadOnlyList<Subscription> _subscriptions;
    private int _completed;
    private int _disposeStarted;

    public FlowDiagnosticCollector(IEnumerable<RuntimeNode> nodes)
    {
        _subscriptions = nodes
            .Select(CreateSubscription)
            .OfType<Subscription>()
            .ToArray();
    }

    public ISourceBlock<RuntimeFlowDiagnostic> Diagnostics => _diagnostics;

    public bool Post(RuntimeFlowDiagnostic diagnostic)
        => _diagnostics.Post(diagnostic);

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
                    _diagnostics.Fault(task.Exception?.InnerException ?? task.Exception!);
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

    private Subscription? CreateSubscription(RuntimeNode node)
    {
        if (node.Node is not IFlowDiagnosticSource source)
        {
            return null;
        }

        var target = new ActionBlock<FlowDiagnostic>(
            diagnostic => _diagnostics.Post(Enrich(node, diagnostic)));
        var link = source.Diagnostics.LinkTo(
            target,
            new DataflowLinkOptions { PropagateCompletion = true });

        return new Subscription(link, target);
    }

    private static RuntimeFlowDiagnostic Enrich(
        RuntimeNode node,
        FlowDiagnostic diagnostic)
        => new()
        {
            NodeAddress = node.Address,
            NodeId = node.Node.Id,
            NodeType = node.Type,
            NodePhase = node.Phase,
            Diagnostic = diagnostic
        };

    private void Complete()
    {
        if (Interlocked.Exchange(ref _completed, 1) == 0)
        {
            _diagnostics.Complete();
        }
    }

    private sealed record Subscription(
        IDisposable Link,
        ITargetBlock<FlowDiagnostic> Target);
}
