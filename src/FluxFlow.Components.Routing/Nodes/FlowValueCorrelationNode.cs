using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Routing.Contracts;
using FluxFlow.Components.Routing.Options;
using FluxFlow.Data;
using FluxFlow.Nodes;

namespace FluxFlow.Components.Routing.Nodes;

/// <summary>Canonical FlowValue correlation with match, timeout, and error results.</summary>
public sealed class FlowValueCorrelationNode : IFlowNode
{
    private readonly FlowCorrelationNode<FlowValue> _inner;
    private readonly BroadcastBlock<
        FlowMessage<FlowResult<FlowCorrelationOutcome<FlowValue>>>> _output =
        new(static message => message);
    private readonly TaskCompletionSource _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly IReadOnlyCollection<IDataflowBlock> _adapters;
    private int _disposed;

    public FlowValueCorrelationNode(
        CorrelationRoutingOptions options,
        Func<FlowValue, string?> keySelector,
        Func<FlowValue, string?> sideSelector,
        string? engineName = null,
        TimeProvider? clock = null)
    {
        _inner = new FlowCorrelationNode<FlowValue>(
            options,
            keySelector,
            sideSelector,
            engineName,
            clock);
        _adapters =
        [
            RoutingFlowResultAdapter.LinkSuccess(
                _inner.Output,
                _output,
                static match => (FlowCorrelationOutcome<FlowValue>)
                    new FlowCorrelationMatchedOutcome<FlowValue> { Match = match },
                static _ => RoutingResultKinds.Matched,
                static match => match.MatchedAt),
            RoutingFlowResultAdapter.LinkSuccess(
                _inner.Timeouts,
                _output,
                static timeout => (FlowCorrelationOutcome<FlowValue>)
                    new FlowCorrelationTimedOutOutcome<FlowValue> { Timeout = timeout },
                static _ => RoutingResultKinds.TimedOut,
                static timeout => timeout.TimedOutAt),
            RoutingFlowResultAdapter.LinkErrors<FlowCorrelationOutcome<FlowValue>>(
                _inner.Errors,
                _output)
        ];
        _ = RoutingFlowResultAdapter.MonitorAsync(
            _inner,
            _adapters,
            _output,
            _completion);
    }

    public ITargetBlock<FlowMessage<FlowValue>> Input => _inner.Input;

    public ISourceBlock<FlowMessage<FlowResult<FlowCorrelationOutcome<FlowValue>>>> Output
        => _output;

    public ISourceBlock<FlowEvent> Events => _inner.Events;

    public Task Completion => _completion.Task;

    public void Complete() => _inner.Complete();

    public void Fault(Exception exception) => _inner.Fault(exception);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        await _inner.DisposeAsync().ConfigureAwait(false);
        try
        {
            await Completion.ConfigureAwait(false);
        }
        catch
        {
            // Completion remains the authoritative unexpected-fault surface.
        }
    }
}
