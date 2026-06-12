using FluxFlow.Engine.Components;
using FluxFlow.Engine.Definitions;
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
    private readonly FlowDiagnosticCollector _diagnosticCollector = new(resources.Concat(workflows.SelectMany(workflow => workflow.Nodes)));
    private readonly FlowErrorCollector _errorCollector = new(resources.Concat(workflows.SelectMany(workflow => workflow.Nodes)));
    private readonly object _stateLock = new();
    private int _disposed;
    private int _linkFailuresAttached;
    private ApplicationState _state = ApplicationState.Idle;

    public IReadOnlyList<RuntimeNode> Resources { get; } = resources ?? throw new ArgumentNullException(nameof(resources));
    public IReadOnlyList<Workflow> Workflows { get; } = workflows ?? throw new ArgumentNullException(nameof(workflows));

    public IEnumerable<RuntimeNode> Nodes => Resources.Concat(Workflows.SelectMany(wf => wf.Nodes));

    public ApplicationState State => _state;

    public ISourceBlock<ApplicationStateChanged> StateChanges => _stateChanges;

    public ISourceBlock<FlowEvent> Events => _eventCollector.Events;

    public ISourceBlock<RuntimeFlowDiagnostic> Diagnostics => _diagnosticCollector.Diagnostics;

    public ISourceBlock<RuntimeFlowError> Errors => _errorCollector.Errors;

    public Task Completion => Task.WhenAll(Nodes.Select(node => node.Node.Completion));

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_stateLock)
        {
            if (_state != ApplicationState.Idle)
            {
                throw new InvalidOperationException(
                    $"Flow application runtime cannot start from state '{_state}'.");
            }
        }

        AttachLinkFailureHandlers();
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
        catch (OperationCanceledException)
        {
            foreach (var workflow in Workflows)
            {
                workflow.CancelStartup();
            }

            SetState(ApplicationState.Stopped);
            throw;
        }
        catch (Exception ex)
        {
            TryFault(ex);
            throw;
        }

        foreach (var workflow in Workflows) workflow.CompleteStartup();
        SetState(ApplicationState.Running);

        var completion = Completion;
        _eventCollector.CompleteWhen(completion);
        _diagnosticCollector.CompleteWhen(completion);
        _errorCollector.CompleteWhen(completion);
        _ = completion.ContinueWith(t =>
        {
            SetState(t.IsFaulted ? ApplicationState.Faulted : ApplicationState.Stopped,
                     t.Exception?.InnerException,
                     preserveFaulted: true);
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
        var faultErrors = TryFault(exception);
        if (faultErrors.Count > 0)
        {
            throw new AggregateException("One or more runtime nodes failed while faulting.", faultErrors);
        }
    }

    private IReadOnlyList<Exception> TryFault(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        SetState(ApplicationState.Faulted, exception);
        var errors = new List<Exception>();
        foreach (var resource in Resources)
        {
            try
            {
                resource.Node.Fault(exception);
            }
            catch (Exception faultException)
            {
                errors.Add(CreateFaultCleanupException(resource.Address, faultException));
            }
        }

        foreach (var workflow in Workflows)
        {
            errors.AddRange(workflow.TryFault(exception));
        }

        return errors;
    }

    private static InvalidOperationException CreateFaultCleanupException(
        NodeAddress address,
        Exception exception)
        => new($"Node '{address}' failed while faulting runtime.", exception);

    private void AttachLinkFailureHandlers()
    {
        if (Interlocked.Exchange(ref _linkFailuresAttached, 1) != 0)
        {
            return;
        }

        foreach (var node in Nodes)
        {
            foreach (var output in node.Outputs)
            {
                var owner = node;
                output.LinkFailed += failure => HandleLinkFailure(owner, failure);
            }
        }
    }

    private void HandleLinkFailure(RuntimeNode node, OutputPortLinkFailure failure)
    {
        var conditionFailed = failure.Reason == OutputPortLinkFailureReason.ConditionFailed;
        _diagnosticCollector.Post(new RuntimeFlowDiagnostic
        {
            NodeAddress = node.Address,
            NodeId = node.Node.Id,
            NodeType = node.Type,
            NodePhase = node.Phase,
            Diagnostic = new FlowDiagnostic
            {
                Timestamp = DateTimeOffset.UtcNow,
                Name = conditionFailed
                    ? RuntimeDiagnosticNames.LinkConditionFailed
                    : RuntimeDiagnosticNames.LinkTargetRejected,
                Level = FlowDiagnosticLevel.Warning,
                Message = conditionFailed
                    ? $"Link condition on output '{failure.Port}' failed; the message was dropped for that link."
                    : $"Output '{failure.Port}' detached a link whose target no longer accepts messages.",
                Exception = failure.Exception,
                Attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["port"] = failure.Port.ToString()
                }
            }
        });

        if (conditionFailed)
        {
            _errorCollector.Post(new RuntimeFlowError
            {
                NodeAddress = node.Address,
                NodeId = node.Node.Id,
                NodeType = node.Type,
                NodePhase = node.Phase,
                Error = new FlowError
                {
                    NodeId = node.Node.Id,
                    Code = FlowErrorCodes.DynamicExpressionFailed,
                    Message = $"Link condition on output '{failure.Port}' failed: {failure.Exception?.Message}",
                    Exception = failure.Exception,
                    Context = failure.Port.ToString()
                }
            });
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        var errors = new List<Exception>();

        foreach (var workflow in Workflows)
        {
            try
            {
                workflow.Dispose();
            }
            catch (Exception exception)
            {
                errors.Add(new InvalidOperationException(
                    $"Runtime failed while disposing workflow '{workflow.Name}'.",
                    exception));
            }
        }

        foreach (var link in _resourceLinks)
        {
            RuntimeCleanup.TryDisposeLink(link, errors, "Runtime");
        }

        foreach (var output in Resources.SelectMany(node => node.Outputs))
        {
            RuntimeCleanup.TryDisposeOutput(output, errors, "Runtime");
        }

        try
        {
            _eventCollector.Dispose();
        }
        catch (Exception exception)
        {
            errors.Add(new InvalidOperationException(
                "Runtime failed while disposing event collector.",
                exception));
        }

        foreach (var resource in Resources)
        {
            RuntimeCleanup.TryDisposeNode(resource, errors, "Runtime");
        }

        RuntimeCleanup.TryDisposeDiagnostics(_diagnosticCollector, errors, "Runtime");
        RuntimeCleanup.TryDisposeErrors(_errorCollector, errors, "Runtime");

        _stateChanges.Complete();
        RuntimeCleanup.ThrowIfErrors(
            "One or more resources failed while disposing application runtime.",
            errors);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        var errors = new List<Exception>();

        foreach (var workflow in Workflows)
        {
            try
            {
                await workflow.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                errors.Add(new InvalidOperationException(
                    $"Runtime failed while disposing workflow '{workflow.Name}'.",
                    exception));
            }
        }

        foreach (var link in _resourceLinks)
        {
            RuntimeCleanup.TryDisposeLink(link, errors, "Runtime");
        }

        foreach (var output in Resources.SelectMany(node => node.Outputs))
        {
            await RuntimeCleanup.TryDisposeOutputAsync(output, errors, "Runtime").ConfigureAwait(false);
        }

        try
        {
            _eventCollector.Dispose();
        }
        catch (Exception exception)
        {
            errors.Add(new InvalidOperationException(
                "Runtime failed while disposing event collector.",
                exception));
        }

        foreach (var resource in Resources)
        {
            await RuntimeCleanup.TryDisposeNodeAsync(resource, errors, "Runtime").ConfigureAwait(false);
        }

        await RuntimeCleanup.TryDisposeDiagnosticsAsync(_diagnosticCollector, errors, "Runtime").ConfigureAwait(false);
        await RuntimeCleanup.TryDisposeErrorsAsync(_errorCollector, errors, "Runtime").ConfigureAwait(false);

        _stateChanges.Complete();
        RuntimeCleanup.ThrowIfErrors(
            "One or more resources failed while disposing application runtime.",
            errors);
    }

    private void SetState(
        ApplicationState next,
        Exception? exception = null,
        bool preserveFaulted = false)
    {
        ApplicationStateChanged? change;
        lock (_stateLock)
        {
            if (preserveFaulted && _state == ApplicationState.Faulted)
            {
                return;
            }

            if (_state == next) return;
            var previous = _state;
            _state = next;
            change = new ApplicationStateChanged(previous, next, exception);
        }
        _stateChanges.Post(change);
    }
}
