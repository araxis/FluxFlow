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
    private readonly FlowDiagnosticCollector _diagnosticCollector = new(nodes ?? throw new ArgumentNullException(nameof(nodes)));
    private readonly FlowErrorCollector _errorCollector = new(nodes ?? throw new ArgumentNullException(nameof(nodes)));
    private readonly object _stateLock = new();
    private int _disposed;
    private WorkflowState _state = WorkflowState.Idle;

    public WorkflowName Name { get; } = name;
    public IReadOnlyList<RuntimeNode> Nodes { get; } = nodes;
    public IReadOnlyList<IDisposable> Links { get; } = links ?? throw new ArgumentNullException(nameof(links));

    public WorkflowState State => _state;

    public ISourceBlock<WorkflowStateChanged> StateChanges => _stateChanges;

    public ISourceBlock<RuntimeFlowDiagnostic> Diagnostics => _diagnosticCollector.Diagnostics;

    public ISourceBlock<RuntimeFlowError> Errors => _errorCollector.Errors;

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
        catch (OperationCanceledException)
        {
            SetState(WorkflowState.Stopped);
            throw;
        }
        catch (Exception ex)
        {
            TryFault(ex);
            throw;
        }

        CompleteStartup();
    }

    internal void BeginStartup() => SetState(WorkflowState.Starting);

    internal void CancelStartup() => SetState(WorkflowState.Stopped);

    internal void CompleteStartup()
    {
        SetState(WorkflowState.Running);
        var completion = Completion;
        _diagnosticCollector.CompleteWhen(completion);
        _errorCollector.CompleteWhen(completion);
        _ = completion.ContinueWith(t =>
        {
            SetState(t.IsFaulted ? WorkflowState.Faulted : WorkflowState.Stopped,
                     t.Exception?.InnerException,
                     preserveFaulted: true);
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
        var faultErrors = TryFault(exception);
        if (faultErrors.Count > 0)
        {
            throw new AggregateException("One or more workflow nodes failed while faulting.", faultErrors);
        }
    }

    internal IReadOnlyList<Exception> TryFault(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        SetState(WorkflowState.Faulted, exception);
        var errors = new List<Exception>();
        foreach (var node in Nodes)
        {
            try
            {
                node.Node.Fault(exception);
            }
            catch (Exception faultException)
            {
                errors.Add(new InvalidOperationException(
                    $"Node '{node.Address}' failed while faulting workflow '{Name}'.",
                    faultException));
            }
        }

        return errors;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        var errors = new List<Exception>();
        var owner = $"Workflow '{Name}'";

        foreach (var link in Links)
        {
            RuntimeCleanup.TryDisposeLink(link, errors, owner);
        }

        foreach (var output in Nodes.SelectMany(node => node.Outputs))
        {
            RuntimeCleanup.TryDisposeOutput(output, errors, owner);
        }

        foreach (var node in Nodes)
        {
            RuntimeCleanup.TryDisposeNode(node, errors, owner);
        }

        RuntimeCleanup.TryDisposeDiagnostics(_diagnosticCollector, errors, owner);
        RuntimeCleanup.TryDisposeErrors(_errorCollector, errors, owner);

        _stateChanges.Complete();
        RuntimeCleanup.ThrowIfErrors(
            $"One or more resources failed while disposing workflow '{Name}'.",
            errors);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        var errors = new List<Exception>();
        var owner = $"Workflow '{Name}'";

        foreach (var link in Links)
        {
            RuntimeCleanup.TryDisposeLink(link, errors, owner);
        }

        foreach (var output in Nodes.SelectMany(node => node.Outputs))
        {
            await RuntimeCleanup.TryDisposeOutputAsync(output, errors, owner).ConfigureAwait(false);
        }

        foreach (var node in Nodes)
        {
            await RuntimeCleanup.TryDisposeNodeAsync(node, errors, owner).ConfigureAwait(false);
        }

        await RuntimeCleanup.TryDisposeDiagnosticsAsync(_diagnosticCollector, errors, owner).ConfigureAwait(false);
        await RuntimeCleanup.TryDisposeErrorsAsync(_errorCollector, errors, owner).ConfigureAwait(false);

        _stateChanges.Complete();
        RuntimeCleanup.ThrowIfErrors(
            $"One or more resources failed while disposing workflow '{Name}'.",
            errors);
    }

    private void SetState(
        WorkflowState next,
        Exception? exception = null,
        bool preserveFaulted = false)
    {
        WorkflowStateChanged? change;
        lock (_stateLock)
        {
            if (preserveFaulted && _state == WorkflowState.Faulted)
            {
                return;
            }

            if (_state == next) return;
            var previous = _state;
            _state = next;
            change = new WorkflowStateChanged(Name, previous, next, exception);
        }
        _stateChanges.Post(change);
    }
}
