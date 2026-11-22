using System.Runtime.ExceptionServices;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Application.Execution;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Application;

/// <summary>Contains workflows and coordinates their execution and lifetime.</summary>
public sealed class FlowApplication : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly TaskCompletionSource _hostCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<FlowWorkflow> _workflows = [];
    private Task? _compose;
    private Task? _start;
    private Task? _stop;
    private Task? _dispose;
    private Task? _completion;
    private volatile bool _stopped;
    private Exception? _failure;

    public IReadOnlyCollection<FlowWorkflow> Workflows
    {
        get { lock (_gate) return Array.AsReadOnly(_workflows.ToArray()); }
    }
    internal IServiceProvider? Services { get; private set; }
    internal Task HostCompletion => _hostCompletion.Task;

    // Retain only native DI workflow instances for lifecycle cleanup, including lazy
    // resolution before Build. All address lookups still go through keyed DI.
    internal void AddWorkflow(FlowWorkflow workflow)
    {
        lock (_gate)
        {
            EnsureOpen();
            _workflows.Add(workflow);
        }
    }

    internal void EnsureOpen()
    {
        if (_stopped) throw new InvalidOperationException("Application has stopped.");
    }

    internal Task ComposeAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return RunOnce(ref _compose, () => ComposeCoreAsync(services, cancellationToken));
        }
    }

    private async Task ComposeCoreAsync(IServiceProvider services, CancellationToken token)
    {
        Services = services;
        try
        {
            EnsureMutable();
            _ = services.GetServices<FlowWorkflow>().ToArray();
            foreach (var workflow in _workflows) workflow.Execution.Compose(token);
            foreach (var workflow in _workflows) workflow.Execution.ResolvePorts();
            foreach (var factory in services.GetServices<LinkFactory>())
            {
                token.ThrowIfCancellationRequested();
                factory(services);
            }
            token.ThrowIfCancellationRequested();
            Validate(this);
        }
        catch (Exception error)
        {
            await CleanupAfterFailureAsync(error).ConfigureAwait(false);
            throw;
        }
    }

    private IEnumerable<FlowLink> Links => _workflows.SelectMany(workflow => workflow.Links);

    internal void AddLink(FlowLink link)
    {
        lock (_gate)
        {
            EnsureMutable();
            if (Services is null ||
                !ReferenceEquals(Services.PortServices(link.InputAddress).GetKeyedService<ITargetBlock<FlowMessage>>(link.InputAddress.Value), link.Input) ||
                !ReferenceEquals(Services.PortServices(link.OutputAddress).GetKeyedService<ISourceBlock<FlowMessage>>(link.OutputAddress.Value), link.Output))
                throw new ArgumentException("Link does not belong to this application.", nameof(link));
            if (!Services.GetOutputPortAddresses().Contains(link.OutputAddress))
                throw new KeyNotFoundException($"Output '{link.OutputAddress}' was not found.");
            Services.GetRequiredKeyedService<FlowWorkflow>(link.OutputAddress.ComponentAddress!.WorkflowName).Links.Add(link);
        }
    }

    private void EnsureMutable()
    {
        if (_start is not null || _stopped)
            throw new InvalidOperationException("Application cannot be changed after starting or closing.");
    }

    private IEnumerable<FlowLink> BusinessLinks(IEnumerable<FlowLink> links)
        => links.Where(link => !Services!.GetWorkflowServices(link.InputAddress.ComponentAddress!.WorkflowName)
            .GetRequiredKeyedService<IFlowNode>(link.InputAddress.ComponentAddress.Value)
            .TerminalInputs.Any(port => ReferenceEquals(port, link.Input)));

    /// <summary>Checks business links for cycles; reply/control inputs are excluded.</summary>
    public static void Validate(FlowApplication application)
    {
        ArgumentNullException.ThrowIfNull(application);
        lock (application._gate)
        {
            if (FlowCycleDetection.HasCycle(application._workflows.SelectMany(workflow => workflow.Execution.Components)
                    .Select(pair => pair.Key.Value).ToArray(),
                application.BusinessLinks(application.Links).Select(link => (link.OutputAddress.ComponentAddress!.Value, link.InputAddress.ComponentAddress!.Value))))
                throw new InvalidOperationException("The application contains a cycle. Feedback execution is not supported.");
        }
    }

    /// <summary>Completion of the current workflow runs. Reacquire after restarting a workflow.</summary>
    public Task Completion
    {
        get { lock (_gate) return _completion ?? throw new InvalidOperationException("Application has not started."); }
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequireBuilt();
            return RunOnce(ref _start, () => StartCoreAsync(cancellationToken));
        }
    }

    private void RequireBuilt()
    {
        EnsureOpen();
        if (_compose is not { IsCompletedSuccessfully: true })
            throw new InvalidOperationException("Build the application successfully before starting it.");
    }

    private async Task StartCoreAsync(CancellationToken token)
    {
        try
        {
            Validate(this);
            var business = BusinessLinks(Links).ToArray();
            // Every route is attached before any source starts producing.
            foreach (var workflow in _workflows)
            {
                workflow.Execution.Prepare(Links, business);
                _ = ObserveFailureAsync(workflow.Execution.Completion);
            }
            RefreshCompletion();
            foreach (var workflow in _workflows) await workflow.Execution.StartAsync(token).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            Fault(error);
            await CleanupAfterFailureAsync(error).ConfigureAwait(false);
            throw;
        }
    }

    internal Task StartWorkflowAsync(FlowWorkflow workflow, CancellationToken token)
    {
        lock (_gate)
        {
            token.ThrowIfCancellationRequested();
            RequireIndependentWorkflow(workflow);
            var previous = workflow.Execution;
            if (previous.IsStopping && !previous.IsStopped)
                throw new InvalidOperationException($"Workflow '{workflow.Name}' is still stopping.");
            if (!previous.IsStopped) return previous.StartAsync(token);
            return RestartAsync(workflow, token);
        }
    }

    private async Task RestartAsync(FlowWorkflow workflow, CancellationToken token)
    {
        workflow.Renew();
        var execution = workflow.Execution;
        try
        {
            execution.Compose(token);
            execution.ResolvePorts();
            // Rebind the existing declarations to fresh native blocks; no configuration reread.
            for (var index = 0; index < workflow.Links.Count; index++)
            {
                var link = workflow.Links[index];
                workflow.Links[index] = new FlowLink(link.OutputAddress,
                    execution.Services.GetRequiredKeyedService<ISourceBlock<FlowMessage>>(link.OutputAddress.Value),
                    link.InputAddress, execution.Services.GetRequiredKeyedService<ITargetBlock<FlowMessage>>(link.InputAddress.Value), link.Condition);
            }
            var business = BusinessLinks(workflow.Links).ToArray();
            execution.Prepare(workflow.Links, business);
            _ = ObserveFailureAsync(execution.Completion);
            RefreshCompletion();
            await execution.StartAsync(token).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            execution.Fault(error);
            try { await execution.StopAsync().ConfigureAwait(false); }
            catch (Exception cleanup) { ThrowCombined(error, cleanup); }
            throw;
        }
    }

    internal Task StopWorkflowAsync(FlowWorkflow workflow, CancellationToken token)
    {
        lock (_gate)
        {
            RequireIndependentWorkflow(workflow);
            var execution = workflow.Execution;
            return AwaitStopAsync(execution.StopAsync(), token, execution.Fault);
        }
    }

    private void RequireIndependentWorkflow(FlowWorkflow workflow)
    {
        RequireBuilt();
        if (_start is not { IsCompletedSuccessfully: true })
            throw new InvalidOperationException("Start the application before controlling individual workflows.");
        if (!_workflows.Contains(workflow)) throw new ArgumentException("Workflow does not belong to this application.", nameof(workflow));
        if (Links.Any(link => link.OutputAddress.ComponentAddress?.WorkflowName != link.InputAddress.ComponentAddress?.WorkflowName
            && (link.OutputAddress.ComponentAddress?.WorkflowName == workflow.Name || link.InputAddress.ComponentAddress?.WorkflowName == workflow.Name)))
            throw new InvalidOperationException($"Workflow '{workflow.Name}' has cross-workflow links. Stop the application as a whole.");
    }

    private void RefreshCompletion()
    {
        _completion = Task.WhenAll(_workflows.Select(workflow => workflow.Execution.Completion));
    }

    private async Task ObserveFailureAsync(Task completion)
    {
        try { await completion.ConfigureAwait(false); }
        catch (Exception error) { Fault(error); }
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate) return AwaitStopAsync(RunOnce(ref _stop, StopCoreAsync), cancellationToken, Fault);
    }

    private async Task StopCoreAsync()
    {
        _stopped = true;
        var stopping = Task.WhenAll(_workflows.Select(workflow => workflow.ExistingExecution?.StopAsync() ?? Task.CompletedTask));
        try
        {
            await stopping.ConfigureAwait(false);
            if (_failure is not null) ExceptionDispatchInfo.Capture(_failure).Throw();
        }
        catch when (stopping.Exception is { InnerExceptions.Count: > 1 })
        {
            throw stopping.Exception.Flatten();
        }
        finally { _hostCompletion.TrySetResult(); }
    }

    private static async Task AwaitStopAsync(Task stop, CancellationToken token, Action<Exception> fault)
    {
        try { await stop.WaitAsync(token).ConfigureAwait(false); }
        catch (OperationCanceledException error) when (token.IsCancellationRequested)
        {
            fault(error);
            throw;
        }
    }

    public void Fault(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        lock (_gate)
        {
            _stopped = true;
            _failure ??= error;
            foreach (var workflow in _workflows)
                if (workflow.ExistingExecution is { IsStopped: false } execution) execution.Fault(error);
            _hostCompletion.TrySetException(error);
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate) return new ValueTask(RunOnce(ref _dispose, DisposeCoreAsync));
    }

    private async Task DisposeCoreAsync()
    {
        try { await StopAsync().ConfigureAwait(false); }
        catch (Exception error) { throw new AggregateException("Application shutdown failed.", error); }
    }

    private async Task CleanupAfterFailureAsync(Exception failure)
    {
        try { await DisposeAsync().ConfigureAwait(false); }
        catch (Exception cleanup) { ThrowCombined(failure, cleanup); }
    }

    private static void ThrowCombined(Exception failure, Exception cleanup)
    {
        IEnumerable<Exception> original = failure is AggregateException first ? first.Flatten().InnerExceptions : [failure];
        IEnumerable<Exception> errors = cleanup is AggregateException second ? second.Flatten().InnerExceptions : [cleanup];
        var additional = errors.Except<Exception>(original, ReferenceEqualityComparer.Instance).ToArray();
        if (additional.Length != 0) throw new AggregateException("Execution and cleanup failed.", [failure, .. additional]);
        ExceptionDispatchInfo.Capture(failure).Throw();
    }

    // Publish the task before invoking user callbacks that could reenter lifecycle methods.
    internal static Task RunOnce(ref Task? task, Func<Task> operation)
    {
        if (task is not null) return task;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        task = completion.Task;
        _ = ExecuteAsync();
        return task;
        async Task ExecuteAsync()
        {
            try { await operation().ConfigureAwait(false); completion.SetResult(); }
            catch (OperationCanceledException error) { completion.SetCanceled(error.CancellationToken); }
            catch (Exception error) { completion.SetException(error); }
        }
    }
}
