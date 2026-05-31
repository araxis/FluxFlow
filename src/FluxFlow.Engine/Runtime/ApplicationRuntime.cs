using FluxFlow.Engine.Components;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Engine.Runtime;

public sealed class ApplicationRuntime(
    IReadOnlyList<RuntimeNode> resources,
    IReadOnlyList<Workflow> workflows,
    IReadOnlyList<RuntimeNode> resourceEntryNodes,
    IReadOnlyList<IDisposable>? resourceLinks = null)
    : IAsyncDisposable, IDisposable
{
    private readonly IReadOnlyList<RuntimeNode> _resourceEntryNodes = resourceEntryNodes ?? throw new ArgumentNullException(nameof(resourceEntryNodes));
    private readonly IReadOnlyList<IDisposable> _resourceLinks = resourceLinks ?? [];
    private readonly BroadcastBlock<ApplicationStateChanged> _stateChanges = new(s => s);
    private readonly FlowEventCollector _eventCollector = new(resources.Concat(workflows.SelectMany(workflow => workflow.Nodes)));
    private readonly object _stateLock = new();
    private bool _disposed;
    private ApplicationState _state = ApplicationState.Idle;

    public IReadOnlyList<RuntimeNode> Resources { get; } = resources ?? throw new ArgumentNullException(nameof(resources));
    public IReadOnlyList<Workflow> Workflows { get; } = workflows ?? throw new ArgumentNullException(nameof(workflows));

    public IEnumerable<RuntimeNode> Nodes => Resources.Concat(Workflows.SelectMany(wf => wf.Nodes));

    public ApplicationState State => _state;

    public ISourceBlock<ApplicationStateChanged> StateChanges => _stateChanges;

    public ISourceBlock<FlowEvent> Events => _eventCollector.Events;

    public Task Completion => Task.WhenAll(Nodes.Select(node => node.Node.Completion));

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        SetState(ApplicationState.Starting);
        foreach (var workflow in Workflows) workflow.BeginStartup();
        try
        {
            var all = Resources.Concat(Workflows.SelectMany(wf => wf.Nodes));
            foreach (var group in all.GroupBy(n => n.Phase).OrderBy(g => g.Key))
            {
                foreach (var node in group)
                {
                    try
                    {
                        await node.Node.StartAsync(cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        throw new ApplicationRuntimeNodeStartException(node.Address, exception);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            SetState(ApplicationState.Faulted, ex);
            throw;
        }

        foreach (var workflow in Workflows) workflow.CompleteStartup();
        SetState(ApplicationState.Running);

        var completion = Completion;
        _eventCollector.CompleteWhen(completion);
        _ = completion.ContinueWith(t =>
        {
            if (_state == ApplicationState.Faulted) return;
            SetState(t.IsFaulted ? ApplicationState.Faulted : ApplicationState.Stopped,
                     t.Exception?.InnerException);
        }, TaskScheduler.Default);
    }

    public void Complete()
    {
        SetState(ApplicationState.Stopping);
        foreach (var node in _resourceEntryNodes)
        {
            node.Node.Complete();
        }

        foreach (var workflow in Workflows)
        {
            workflow.Complete();
        }
    }

    public void Fault(Exception exception)
    {
        SetState(ApplicationState.Faulted, exception);
        foreach (var resource in Resources)
        {
            resource.Node.Fault(exception);
        }

        foreach (var workflow in Workflows)
        {
            workflow.Fault(exception);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (var workflow in Workflows)
        {
            workflow.Dispose();
        }

        foreach (var link in _resourceLinks)
        {
            link.Dispose();
        }

        _eventCollector.Dispose();

        foreach (var disposable in Resources.Select(node => node.Node).OfType<IDisposable>())
        {
            disposable.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (var workflow in Workflows)
        {
            await workflow.DisposeAsync().ConfigureAwait(false);
        }

        foreach (var link in _resourceLinks)
        {
            link.Dispose();
        }

        _eventCollector.Dispose();

        foreach (var disposable in Resources.Select(node => node.Node).OfType<IAsyncDisposable>())
        {
            await disposable.DisposeAsync().ConfigureAwait(false);
        }
    }

    private void SetState(ApplicationState next, Exception? exception = null)
    {
        ApplicationStateChanged? change;
        lock (_stateLock)
        {
            if (_state == next) return;
            var previous = _state;
            _state = next;
            change = new ApplicationStateChanged(previous, next, exception);
        }
        _stateChanges.Post(change);
    }
}
