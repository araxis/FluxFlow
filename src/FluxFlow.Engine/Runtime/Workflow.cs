using FluxFlow.Engine.Definitions;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Engine.Runtime;

public sealed class Workflow(
    WorkflowName name,
    IReadOnlyList<RuntimeNode> nodes,
    IReadOnlyList<IDisposable> links,
    IReadOnlyList<RuntimeNode> entryNodes)
    : IAsyncDisposable, IDisposable
{
    private readonly IReadOnlyList<RuntimeNode> _entryNodes = entryNodes ?? throw new ArgumentNullException(nameof(entryNodes));
    private readonly BroadcastBlock<WorkflowStateChanged> _stateChanges = new(s => s);
    private readonly object _stateLock = new();
    private bool _disposed;
    private WorkflowState _state = WorkflowState.Idle;

    public WorkflowName Name { get; } = name;
    public IReadOnlyList<RuntimeNode> Nodes { get; } = nodes ?? throw new ArgumentNullException(nameof(nodes));
    public IReadOnlyList<IDisposable> Links { get; } = links ?? throw new ArgumentNullException(nameof(links));

    public WorkflowState State => _state;

    public ISourceBlock<WorkflowStateChanged> StateChanges => _stateChanges;

    public Task Completion => Task.WhenAll(Nodes.Select(node => node.Node.Completion));

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        BeginStartup();
        try
        {
            foreach (var group in Nodes.GroupBy(n => n.Phase).OrderBy(g => g.Key))
            {
                foreach (var node in group)
                {
                    await node.Node.StartAsync(cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex)
        {
            SetState(WorkflowState.Faulted, ex);
            throw;
        }

        CompleteStartup();
    }

    internal void BeginStartup() => SetState(WorkflowState.Starting);

    internal void CompleteStartup()
    {
        SetState(WorkflowState.Running);
        var completion = Completion;
        _ = completion.ContinueWith(t =>
        {
            if (_state == WorkflowState.Faulted) return;
            SetState(t.IsFaulted ? WorkflowState.Faulted : WorkflowState.Stopped,
                     t.Exception?.InnerException);
        }, TaskScheduler.Default);
    }

    public void Complete()
    {
        SetState(WorkflowState.Stopping);
        foreach (var node in _entryNodes)
        {
            node.Node.Complete();
        }
    }

    public void Fault(Exception exception)
    {
        SetState(WorkflowState.Faulted, exception);
        foreach (var node in Nodes)
        {
            node.Node.Fault(exception);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (var link in Links)
        {
            link.Dispose();
        }

        foreach (var disposable in Nodes.Select(node => node.Node).OfType<IDisposable>())
        {
            disposable.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        Dispose();

        foreach (var disposable in Nodes.Select(node => node.Node).OfType<IAsyncDisposable>())
        {
            await disposable.DisposeAsync().ConfigureAwait(false);
        }
    }

    private void SetState(WorkflowState next, Exception? exception = null)
    {
        WorkflowStateChanged? change;
        lock (_stateLock)
        {
            if (_state == next) return;
            var previous = _state;
            _state = next;
            change = new WorkflowStateChanged(Name, previous, next, exception);
        }
        _stateChanges.Post(change);
    }
}
