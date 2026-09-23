using System.Runtime.ExceptionServices;
using FluxFlow.Core.Nodes;

namespace FluxFlow.Core.Execution;

/// <summary>Shared component startup, stopping and first-failure coordination; no routing or ownership policy.</summary>
internal sealed class FlowLifetime
{
    private readonly IFlowNode[] _nodes;
    private readonly IFlowNode[] _stopNodes;
    private readonly Action<Exception>? _beforeFault;
    private Exception? _failure;
    private int _stopRequested;

    internal FlowLifetime(IEnumerable<IFlowNode> nodes, IEnumerable<IFlowNode> entryNodes,
        Action<Exception>? beforeFault = null)
    {
        _nodes = nodes.ToArray();
        var entries = new HashSet<IFlowNode>(entryNodes, ReferenceEqualityComparer.Instance);
        // A source with a control input must stop external production too.
        _stopNodes = _nodes.Where(node => entries.Contains(node) || node is IFlowSource).ToArray();
        _beforeFault = beforeFault;
    }

    internal Exception? Failure => Volatile.Read(ref _failure);

    internal async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            foreach (var source in _nodes.OfType<IFlowSource>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (Failure is { } failure) ExceptionDispatchInfo.Capture(failure).Throw();
                await source.StartAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception error)
        {
            Fault(error);
            throw;
        }
    }

    internal void Stop()
    {
        if (Interlocked.Exchange(ref _stopRequested, 1) != 0 || Failure is not null) return;
        try
        {
            foreach (var node in _stopNodes)
            {
                if (node is IFlowSource source) source.StopProduction();
                else node.Complete();
            }
        }
        catch (Exception error)
        {
            Fault(error);
            throw;
        }
    }

    internal void Fault(Exception error)
    {
        if (Interlocked.CompareExchange(ref _failure, error, null) is not null) return;
        // Delivery must stop before component faults release native reservations.
        try { _beforeFault?.Invoke(error); }
        catch { /* Still stop every component and retain the initiating failure. */ }
        foreach (var node in _nodes)
        {
            try { node.Fault(error); }
            catch { /* A failing fault callback cannot strand its siblings. */ }
        }
    }

    internal async Task CompleteAfterAsync(IFlowNode node, IEnumerable<Task> producers)
    {
        try
        {
            await Task.WhenAll(producers).ConfigureAwait(false);
            if (node is IFlowSource source) source.StopProduction();
            else node.Complete();
        }
        catch (Exception error) { Fault(error); }
    }

    internal async Task WaitForCompletionAsync(IEnumerable<Task> additionalWork)
    {
        // A composed completion can include extra work but cannot hide its node's fault.
        var work = _nodes.Select(node => node.Completion).Concat(additionalWork).Distinct();
        await Task.WhenAll(work.Select(ObserveAsync)).ConfigureAwait(false);
        if (Failure is { } failure) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    internal async Task CompleteControlsAfterAsync(IEnumerable<Task> businessWork)
    {
        var controls = _nodes.SelectMany(node => node.TerminalInputs).ToArray();
        if (controls.Length == 0) return;
        try
        {
            await Task.WhenAll(businessWork).ConfigureAwait(false);
            foreach (var control in controls) control.Complete();
        }
        catch (Exception error) { Fault(error); }
    }

    private async Task ObserveAsync(Task completion)
    {
        try { await completion.ConfigureAwait(false); }
        catch (Exception error) { Fault(error); }
    }
}
