using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Application.Addressing;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Application.Execution;

// State owned by one run, released together through its native DI scope.
internal sealed class WorkflowExecution(string name, AsyncServiceScope scope)
{
    private readonly List<IDisposable> _links = [];
    private ApplicationLifetime? _lifetime;
    private Task? _completion;
    private Task? _start;
    private Task? _stop;
    private bool _disposed;
    internal ConcurrentQueue<KeyValuePair<ComponentAddress, IFlowNode>> Components { get; } = new();
    internal IServiceProvider Services => !_disposed ? scope.ServiceProvider
        : throw new InvalidOperationException($"Workflow '{name}' has stopped. Start it before resolving services.");
    internal Task Completion => _completion ?? throw new InvalidOperationException($"Workflow '{name}' has not started.");
    internal bool IsStopped => _stop?.IsCompleted == true;
    internal bool CanRestart => _stop?.IsCompletedSuccessfully == true;
    internal bool IsStopping => _stop is not null;

    internal void Compose(CancellationToken token)
    {
        foreach (var address in Services.GetKeyedServices<ComponentAddress>(name))
        {
            token.ThrowIfCancellationRequested();
            _ = Services.GetRequiredKeyedService<IFlowNode>(address.Value);
            foreach (var events in Services.GetKeyedServices<string>((address.Value, typeof(ComponentEventBridge))))
                _ = Services.GetRequiredKeyedService<ComponentEventBridge>(events);
        }
    }

    internal void ResolvePorts()
    {
        _ = Ports<ITargetBlock<FlowMessage>>().ToArray();
        _ = Ports<ISourceBlock<FlowMessage>>().ToArray();
    }

    private IEnumerable<T> Ports<T>() where T : class, IDataflowBlock
        => Services.GetPortRegistrations<T>()
            .Where(address => address.ComponentAddress?.WorkflowName == name)
            .Select(address => Services.GetRequiredKeyedService<T>(address.Value));

    internal void Prepare(IEnumerable<FlowLink> links, IEnumerable<FlowLink> businessLinks)
    {
        if (_lifetime is not null) return;
        var producers = businessLinks.ToLookup(link => link.InputAddress.ComponentAddress!, link => link.Output.Completion);
        _lifetime = new ApplicationLifetime(Components.Select(pair => pair.Value),
            Components.Where(pair => !producers.Contains(pair.Key)).Select(pair => pair.Value));
        foreach (var link in links.Where(link => link.OutputAddress.ComponentAddress?.WorkflowName == name))
            _links.Add(link.Condition is { } condition
                ? link.Output.LinkTo(link.Input, new DataflowLinkOptions(), condition)
                : link.Output.LinkTo(link.Input, new DataflowLinkOptions()));
        var outputs = Ports<ISourceBlock<FlowMessage>>().Select(port => port.Completion).Distinct().ToArray();
        var tasks = outputs.Concat(Ports<ITargetBlock<FlowMessage>>().Select(port => port.Completion)).ToList();
        tasks.AddRange(Components.SelectMany(pair => Services.GetKeyedServices<string>((pair.Key.Value, typeof(ComponentEventBridge))))
            .Select(address => Services.GetRequiredKeyedService<ComponentEventBridge>(address).Completion));
        foreach (var (address, component) in Components)
            if (producers.Contains(address)) tasks.Add(_lifetime.CompleteAfterAsync(component, producers[address].Distinct()));
        // Event bridges remain open until their component completes, including its
        // terminal inputs. Waiting for them here would make shutdown circular.
        var eventPorts = Components.SelectMany(pair => Services.GetKeyedServices<string>((pair.Key.Value, typeof(ComponentEventBridge))))
            .ToHashSet(StringComparer.Ordinal);
        var businessOutputs = Services.GetPortRegistrations<ISourceBlock<FlowMessage>>()
            .Where(address => address.ComponentAddress?.WorkflowName == name && !eventPorts.Contains(address.Value))
            .Select(address => Services.GetRequiredKeyedService<ISourceBlock<FlowMessage>>(address.Value).Completion).Distinct();
        tasks.Add(_lifetime.CompleteControlsAfterAsync(businessOutputs));
        _completion = _lifetime.WaitForCompletionAsync(tasks);
    }

    internal Task StartAsync(CancellationToken token)
        => FlowApplication.RunOnce(ref _start, () => (_lifetime ?? throw new InvalidOperationException("Prepare links before starting.")).StartAsync(token));

    internal void Fault(Exception error) => Lifetime().Fault(error);

    // Called while the application's lifecycle gate is held, before waiting for any workflow.
    internal Task StopAsync() => FlowApplication.RunOnce(ref _stop, StopCoreAsync);

    private ApplicationLifetime Lifetime()
        => _lifetime ??= new ApplicationLifetime(Components.Select(pair => pair.Value), Components.Select(pair => pair.Value));

    private async Task StopCoreAsync()
    {
        List<Exception> errors = [];
        var lifetime = Lifetime();
        try { lifetime.Stop(); }
        catch (Exception error) { errors.Add(error); }
        try
        {
            await (_completion ??= lifetime.WaitForCompletionAsync([lifetime.CompleteControlsAfterAsync([])]))
                .ConfigureAwait(false);
        }
        catch (Exception error) { errors.Add(error); }
        try { if (_start is not null) await _start.ConfigureAwait(false); }
        catch (Exception error) { errors.Add(error); }
        foreach (var link in _links)
        {
            try { link.Dispose(); }
            catch (Exception error) { errors.Add(error); }
        }
        _disposed = true;
        try { await scope.DisposeAsync().ConfigureAwait(false); }
        catch (Exception error) { errors.Add(error); }
        var failures = errors.SelectMany(error => error is AggregateException aggregate
                ? (IEnumerable<Exception>)aggregate.Flatten().InnerExceptions : [error])
            .Distinct<Exception>(ReferenceEqualityComparer.Instance).ToArray();
        if (failures.Length == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Length > 1) throw new AggregateException($"Workflow '{name}' shutdown failed.", failures);
    }
}
