using System.Runtime.ExceptionServices;

namespace FluxFlow.Core;

/// <summary>Coordinates graph lifetime and fan-in; never processes component payloads.</summary>
internal sealed class FlowExecution : IAsyncDisposable
{
    private readonly Dictionary<FlowOutputEndpoint, FlowRouter> _routers = [];
    private readonly FlowRequestReply _exchanges;
    private readonly FlowLifetime _lifetime;
    private Exception? _exchangeStopFailure;

    public FlowExecution(FlowGraphSnapshot graph, int linkCapacity, FlowRequestReply exchanges)
    {
        _exchanges = exchanges;
        var entryComponents = graph.DependencyLayers.FirstOrDefault() ?? [];
        _lifetime = new FlowLifetime(graph.Components.Select(component => component.Instance),
            entryComponents.Select(component => component.Instance), FaultRouting);

        try
        {
            foreach (var output in graph.Components.SelectMany(c => c.Outputs.Values))
                _routers.Add(output, new FlowRouter(output, graph.Links.Where(l => l.Output == output),
                    graph.DiscardUnmatched.Contains(output), linkCapacity, Fault,
                    message => exchanges.Reply(output.Address, message)));
            // Populate every route owner before an already-running native source can emit.
            foreach (var router in _routers.Values) router.ConnectSource();
        }
        catch (Exception error)
        {
            Fault(error);
            foreach (var router in _routers.Values) router.Detach();
            throw;
        }

        var completionTasks = _routers.Values.Select(router => router.Completion).ToList();
        completionTasks.AddRange(graph.Components.Select(component => component.Node.Completion));
        foreach (var component in graph.Components.Except(entryComponents))
        {
            var producers = graph.Links.Where(l => l.Input.Owner == component && !l.Input.IsControl)
                .Select(l => _routers[l.Output].Completion)
                .Concat(graph.CallDependencies.Where(d => d.Target == component).Select(d => d.Caller.Node.Completion))
                .Distinct().ToArray();
            completionTasks.Add(_lifetime.CompleteAfterAsync(component.Instance, producers));
        }
        completionTasks.Add(_lifetime.CompleteControlsAfterAsync(_routers.Values.Select(router => router.Completion)));
        Completion = FinishAsync(completionTasks);
    }

    public Task Completion { get; }

    internal void EnableRequestReplies(FlowOutputEndpoint output) => _routers[output].EnableRequestReplies();

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Component blocks and links are ready before any source starts producing.
        await _lifetime.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Stop()
    {
        _lifetime.Stop();
    }

    public void Fault(Exception error)
    {
        _lifetime.Fault(error);
    }

    private void FaultRouting(Exception error)
    {
        try { _exchanges.Stop(error); }
        catch (Exception cleanupError) { _exchangeStopFailure = cleanupError; }
        // Stop business delivery before component faults release held reservations.
        foreach (var router in _routers.Values)
        {
            try { router.Fault(error); }
            catch { /* Continue stopping components; preserve the initiating failure. */ }
        }
    }

    public async ValueTask DisposeAsync()
    {
        List<Exception>? errors = null;
        foreach (var router in _routers.Values)
        {
            try { await router.DisposeAsync().ConfigureAwait(false); }
            catch (Exception error) { (errors ??= []).Add(error); }
        }
        if (errors is not null) throw new AggregateException("Route cleanup failed.", errors);
    }

    private async Task FinishAsync(IEnumerable<Task> tasks)
    {
        Exception? failure = null;
        try { await _lifetime.WaitForCompletionAsync(tasks).ConfigureAwait(false); }
        catch (Exception error) { failure = error; }
        try { _exchanges.Stop(_lifetime.Failure); }
        catch (Exception cleanupError) { _exchangeStopFailure ??= cleanupError; }

        if (failure is not null && _exchangeStopFailure is not null && !ReferenceEquals(failure, _exchangeStopFailure))
            throw new AggregateException(failure, _exchangeStopFailure);
        if ((failure ?? _exchangeStopFailure) is { } terminalError)
            ExceptionDispatchInfo.Capture(terminalError).Throw();
    }
}
