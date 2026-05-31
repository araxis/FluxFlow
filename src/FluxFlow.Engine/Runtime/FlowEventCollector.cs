using FluxFlow.Engine.Components;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Engine.Runtime;

internal sealed class FlowEventCollector : IDisposable
{
    private readonly BroadcastBlock<FlowEvent> _events = new(static flowEvent => flowEvent);
    private readonly IReadOnlyList<IDisposable> _links;
    private readonly IReadOnlyList<Task> _sourceCompletions;
    private int _completed;

    public FlowEventCollector(IEnumerable<RuntimeNode> nodes)
    {
        var eventSources = nodes
            .Select(node => node.Node)
            .OfType<IFlowEventSource>()
            .ToArray();

        _links = eventSources
            .Select(source => source.Events.LinkTo(_events))
            .ToArray();

        _sourceCompletions = eventSources
            .Select(source => source.Events.Completion)
            .ToArray();
    }

    public ISourceBlock<FlowEvent> Events => _events;

    public void CompleteWhen(Task runtimeCompletion)
    {
        _ = Task.WhenAll(_sourceCompletions.Append(runtimeCompletion)).ContinueWith(
            task =>
            {
                if (task.IsFaulted)
                {
                    ((IDataflowBlock)_events).Fault(task.Exception?.InnerException ?? task.Exception!);
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
        foreach (var link in _links)
        {
            link.Dispose();
        }

        Complete();
    }

    private void Complete()
    {
        if (Interlocked.Exchange(ref _completed, 1) == 0)
        {
            _events.Complete();
        }
    }
}
