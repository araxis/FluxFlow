using System.Runtime.ExceptionServices;
using FluxFlow.Core.Application;
using FluxFlow.Core.Execution;
using FluxFlow.Core.Nodes;
using System.Collections.ObjectModel;
using FluxFlow.Core.Addressing;
using FluxFlow.Core.Messaging;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Core.Application;

/// <summary>Contains workflows and coordinates component execution and lifetime.</summary>
public sealed class FlowApplication : IAsyncDisposable
{
    internal Dictionary<string, FlowWorkflow> WorkflowMap { get; } = new(StringComparer.Ordinal);
    internal object Gate { get; } = new();

    /// <summary>A read-only snapshot of the application's workflows, indexed by name.</summary>
    public IReadOnlyDictionary<string, FlowWorkflow> Workflows
    {
        get
        {
            lock (Gate)
                return new ReadOnlyDictionary<string, FlowWorkflow>(
                    new Dictionary<string, FlowWorkflow>(WorkflowMap, StringComparer.Ordinal));
        }
    }

    private readonly CancellationTokenSource _ingressStopped = new();
    private readonly Dictionary<string, FlowRouter> _routers = new(StringComparer.Ordinal);
    private int _linkCapacity;
    internal int LinkCapacity => _linkCapacity;
    private FlowLifetime? _lifetime;
    private Task? _completion;
    private Task? _compose;
    private Task? _start;
    private Task? _stop;
    private Task? _dispose;

    public FlowApplication(int linkCapacity = 128)
    {
        if (linkCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(linkCapacity));
        _linkCapacity = linkCapacity;
    }

    internal IServiceProvider? Services { get; private set; }

    private FlowWorkflow AddWorkflow(string name)
    {
        lock (Gate)
        {
            EnsureMutable();
            ApplicationAddress.RequireWorkflowName(name, nameof(name));
            var workflow = new FlowWorkflow(name);
            WorkflowMap.Add(name, workflow);
            return workflow;
        }
    }
    internal IEnumerable<KeyValuePair<string, IFlowNode>> Components
        => WorkflowMap.Values.SelectMany(workflow => workflow.Components);

    internal void EnsureMutable()
    {
        if (_start is not null || _ingressStopped.IsCancellationRequested)
            throw new InvalidOperationException("Application cannot be changed after starting or closing.");
    }

    internal Task ComposeAsync(IServiceProvider services, int linkCapacity, CancellationToken cancellationToken)
    {
        lock (Gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (linkCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(linkCapacity));
            if (_compose is not null && _linkCapacity != linkCapacity)
                throw new InvalidOperationException("Application is already composed with a different link capacity.");
            _linkCapacity = linkCapacity;
            return RunOnce(ref _compose, () => ComposeCoreAsync(services, cancellationToken));
        }
    }

    private async Task ComposeCoreAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        Services = services;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            AddComponents(services, cancellationToken);
            MakeLinks(services, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            Validate(this);
        }
        catch (Exception buildError)
        {
            try { await DisposeAsync().ConfigureAwait(false); }
            catch (Exception cleanupError)
            {
                throw new AggregateException("Application construction and cleanup failed.", buildError, cleanupError);
            }
            throw;
        }
    }

    private void AddComponents(IServiceProvider services, CancellationToken cancellationToken)
    {
        var workflows = services.GetKeyedService<IReadOnlyDictionary<string, string[]>>(typeof(FlowWorkflow));
        foreach (var (workflowName, names) in workflows ?? new Dictionary<string, string[]>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var workflow = AddWorkflow(workflowName);
            foreach (var name in names)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var componentAddress = ApplicationAddress.WorkflowComponent(workflowName, name);
                var address = componentAddress.Value;
                var node = services.GetRequiredKeyedService<IFlowNode>(address);
                workflow.Components.Add(address, node);
                foreach (var port in services.GetPortRegistrations<ITargetBlock<FlowMessage>>()
                    .Where(port => ApplicationAddress.Parse(port.Address).ComponentAddress == componentAddress))
                    _ = services.GetRequiredKeyedService<ITargetBlock<FlowMessage>>(port.Address);
                foreach (var port in services.GetPortRegistrations<ISourceBlock<FlowMessage>>()
                    .Where(port => ApplicationAddress.Parse(port.Address).ComponentAddress == componentAddress))
                    _ = services.GetRequiredKeyedService<ISourceBlock<FlowMessage>>(port.Address);
                foreach (var eventAddress in services.GetKeyedService<IReadOnlyCollection<string>>((address, typeof(ComponentActivityBridge))) ?? [])
                {
                    workflow.EventBridges.Add(services.GetRequiredKeyedService<ComponentActivityBridge>(eventAddress));
                }
            }
        }
    }

    private void MakeLinks(IServiceProvider services, CancellationToken cancellationToken)
    {
        foreach (var factory in services.GetServices<LinkFactory>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            factory(services);
        }
    }

    internal void AddLink(string address, FlowLink link)
    {
        lock (Gate)
        {
            EnsureMutable();
            var workflow = WorkflowMap[ApplicationAddress.Parse(address).WorkflowName!];
            if (Services is null ||
                !ReferenceEquals(Services.GetKeyedService<ITargetBlock<FlowMessage>>(link.InputAddress), link.Input) ||
                link.OutputAddress != address ||
                !ReferenceEquals(Services.GetKeyedService<ISourceBlock<FlowMessage>>(address), link.Output))
                throw new ArgumentException("Link does not belong to this application.", nameof(link));
            if (link.IsFallback && workflow.Links.Any(route => route.OutputAddress == address && route.IsFallback))
                throw new InvalidOperationException($"Output '{address}' already has a fallback.");
            workflow.Links.Add(link);
        }
    }



    private void Initialize()
    {
        var components = Components.ToArray();
        var outputs = Services!.GetPortRegistrations<ISourceBlock<FlowMessage>>();
        var producers = BusinessLinks().ToLookup(link => link.Target, link => link.Output, StringComparer.Ordinal);
        _lifetime = new FlowLifetime(components.Select(component => component.Value),
            components.Where(component => !producers.Contains(component.Key)).Select(component => component.Value), FaultRouting);

        try
        {
            foreach (var (address, _) in outputs)
            {
                var workflow = WorkflowMap[ApplicationAddress.Parse(address).WorkflowName!];
                _routers.Add(address, new FlowRouter(address, Services!.GetRequiredKeyedService<ISourceBlock<FlowMessage>>(address),
                    workflow.Links.Where(link => link.OutputAddress == address).ToArray(),
                    _linkCapacity, Fault));
            }
            // Populate every route owner before an already-running native source can emit.
            foreach (var router in _routers.Values) router.ConnectSource();
        }
        catch (Exception error)
        {
            Fault(error);
            foreach (var router in _routers.Values) router.Detach();
            _completion = _lifetime.WaitForCompletionAsync(components.Select(component => component.Value.Completion)
                .Append(_lifetime.CompleteControlsAfterAsync([])));
            throw;
        }

        var completionTasks = _routers.Values.Select(router => router.Completion).ToList();
        completionTasks.AddRange(components.Select(component => component.Value.Completion));
        completionTasks.AddRange(Services!.GetPortRegistrations<ITargetBlock<FlowMessage>>()
            .Select(input => Services!.GetRequiredKeyedService<ITargetBlock<FlowMessage>>(input.Address).Completion).Distinct());
        foreach (var (address, component) in components)
        {
            if (!producers.Contains(address)) continue;
            completionTasks.Add(_lifetime.CompleteAfterAsync(component,
                producers[address].Select(output => _routers[output].Completion).Distinct()));
        }
        completionTasks.Add(_lifetime.CompleteControlsAfterAsync(_routers.Values.Select(router => router.Completion)));
        _completion = _lifetime.WaitForCompletionAsync(completionTasks);
    }

    /// <summary>Checks whether the application's links support orderly execution and shutdown.</summary>
    public static void Validate(FlowApplication application)
    {
        ArgumentNullException.ThrowIfNull(application);
        lock (application.Gate)
        {
            if (FlowCycleDetection.HasCycle(application.Components.Select(pair => pair.Key).ToArray(),
                application.BusinessLinks().Select(link => (ApplicationAddress.Parse(link.Output).ComponentAddress!.Value, link.Target))))
                throw new InvalidOperationException("The application contains a cycle. Feedback execution is not supported.");
        }
    }

    private IEnumerable<(string Output, string Target)> BusinessLinks()
    {
        foreach (var link in WorkflowMap.Values.SelectMany(workflow => workflow.Links))
        {
            var target = ApplicationAddress.Parse(link.InputAddress).ComponentAddress!.Value;
            var component = Services!.GetRequiredKeyedService<IFlowNode>(target);
            if (!component.TerminalInputs.Any(port => ReferenceEquals(port, link.Input)))
                yield return (link.OutputAddress, target);
        }
    }

    public Task Completion
    {
        get
        {
            lock (Gate)
                return _completion ?? throw new InvalidOperationException("Application has not started.");
        }
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        lock (Gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_ingressStopped.IsCancellationRequested) throw new InvalidOperationException("Application has stopped.");
            return RunOnce(ref _start, () => StartCoreAsync(cancellationToken));
        }
    }

    private async Task StartCoreAsync(CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            _ingressStopped.Token.ThrowIfCancellationRequested();
            Validate(this);
            Initialize();
            await _lifetime!.StartAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception startupError)
        {
            try { await DisposeAsync().ConfigureAwait(false); }
            catch (AggregateException shutdownErrors)
            {
                var cleanupErrors = shutdownErrors.Flatten().InnerExceptions
                    .Where(error => !ReferenceEquals(error, startupError)).ToArray();
                if (cleanupErrors.Length != 0)
                    startupError = new AggregateException("Application startup and cleanup failed.", [startupError, .. cleanupErrors]);
            }
            catch (Exception cleanupError)
            {
                if (!ReferenceEquals(cleanupError, startupError))
                    startupError = new AggregateException("Application startup and cleanup failed.", startupError, cleanupError);
            }
            ExceptionDispatchInfo.Capture(startupError).Throw();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        Task stopping;
        lock (Gate)
        {
            stopping = RunOnce(ref _stop, StopCoreAsync);
        }
        try { await stopping.WaitAsync(cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException error) when (cancellationToken.IsCancellationRequested)
        {
            Fault(error);
            throw;
        }
    }

    private async Task StopCoreAsync()
    {
        List<Exception>? errors = null;
        try { _ingressStopped.Cancel(); }
        catch (Exception error) { (errors ??= []).Add(error); }
        var components = Components.Select(component => component.Value).ToArray();
        var lifetime = _lifetime ?? new FlowLifetime(components, components);
        try { lifetime.Stop(); }
        catch (Exception error) { (errors ??= []).Add(error); }
        try
        {
            await (_completion ?? lifetime.WaitForCompletionAsync(components.Select(component => component.Completion)
                .Append(lifetime.CompleteControlsAfterAsync([])))).ConfigureAwait(false);
        }
        catch (Exception error) { (errors ??= []).Add(error); }
        ThrowShutdownFailures(errors);
    }

    public void Fault(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        lock (Gate)
        {
            List<Exception>? errors = null;
            try { _ingressStopped.Cancel(); }
            catch (Exception cleanupError) { (errors ??= []).Add(cleanupError); }
            _lifetime?.Fault(error);
            ThrowShutdownFailures(errors);
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (Gate) return new ValueTask(RunOnce(ref _dispose, DisposeCoreAsync));
    }

    // Called under Gate. Publish the shared task before callbacks can reenter.
    private static Task RunOnce(ref Task? task, Func<Task> operation)
    {
        if (task is not null) return task;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        task = completion.Task;
        _ = ExecuteAsync();
        return task;

        async Task ExecuteAsync()
        {
            try
            {
                await operation().ConfigureAwait(false);
                completion.SetResult();
            }
            catch (OperationCanceledException error) { completion.SetCanceled(error.CancellationToken); }
            catch (Exception error) { completion.SetException(error); }
        }
    }

    private async Task DisposeCoreAsync()
    {
        List<Exception>? errors = null;
        try { await StopAsync().ConfigureAwait(false); }
        catch (Exception error) { (errors ??= []).Add(error); }
        foreach (var router in _routers.Values)
        {
            try { await router.DisposeAsync().ConfigureAwait(false); }
            catch (Exception error) { (errors ??= []).Add(error); }
        }
        foreach (var bridge in WorkflowMap.Values.SelectMany(workflow => workflow.EventBridges))
        {
            try
            {
                await bridge.Completion.ConfigureAwait(false);
            }
            catch (Exception error) { (errors ??= []).Add(error); }
        }
        _ingressStopped.Dispose();
        if (errors is not null) throw new AggregateException("Application shutdown failed.", errors);
    }

    private static void ThrowShutdownFailures(List<Exception>? errors)
    {
        if (errors is null) return;
        var distinct = errors.Distinct<Exception>(ReferenceEqualityComparer.Instance).ToArray();
        if (distinct.Length == 1) ExceptionDispatchInfo.Capture(distinct[0]).Throw();
        throw new AggregateException("Application shutdown failed.", distinct);
    }
    private void FaultRouting(Exception error)
    {
        // Stop business delivery before component faults release held reservations.
        foreach (var router in _routers.Values)
        {
            try { router.Fault(error); }
            catch { /* Continue stopping components; preserve the initiating failure. */ }
        }
    }

}
