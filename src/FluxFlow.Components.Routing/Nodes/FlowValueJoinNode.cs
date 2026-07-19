using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Routing.Contracts;
using FluxFlow.Components.Routing.Options;
using FluxFlow.Data;
using FluxFlow.Nodes;

namespace FluxFlow.Components.Routing.Nodes;

/// <summary>Canonical two-input FlowValue join with one normal result output.</summary>
public sealed class FlowValueJoinNode : IFlowNode
{
    private readonly FlowJoinNode<FlowValue, FlowValue> _inner;
    private readonly BroadcastBlock<
        FlowMessage<FlowResult<FlowJoinOutcome<FlowValue, FlowValue>>>> _output =
        new(static message => message);
    private readonly TaskCompletionSource _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly IReadOnlyCollection<IDataflowBlock> _adapters;
    private int _disposed;

    public FlowValueJoinNode(
        JoinRoutingOptions options,
        Func<FlowValue, string?> leftKeySelector,
        Func<FlowValue, string?> rightKeySelector,
        string? engineName = null,
        TimeProvider? clock = null)
    {
        _inner = new FlowJoinNode<FlowValue, FlowValue>(
            options,
            leftKeySelector,
            rightKeySelector,
            engineName,
            clock);
        _adapters =
        [
            RoutingFlowResultAdapter.LinkSuccess(
                _inner.Output,
                _output,
                static match => (FlowJoinOutcome<FlowValue, FlowValue>)
                    new FlowJoinMatchedOutcome<FlowValue, FlowValue> { Match = match },
                static _ => RoutingResultKinds.Matched,
                static match => match.JoinedAt),
            RoutingFlowResultAdapter.LinkSuccess(
                _inner.Timeouts,
                _output,
                static timeout => (FlowJoinOutcome<FlowValue, FlowValue>)
                    new FlowJoinTimedOutOutcome<FlowValue, FlowValue> { Timeout = timeout },
                static _ => RoutingResultKinds.TimedOut,
                static timeout => timeout.TimedOutAt),
            RoutingFlowResultAdapter.LinkErrors<FlowJoinOutcome<FlowValue, FlowValue>>(
                _inner.Errors,
                _output)
        ];
        _ = RoutingFlowResultAdapter.MonitorAsync(
            _inner,
            _adapters,
            _output,
            _completion);
    }

    public ITargetBlock<FlowMessage<FlowValue>> Left => _inner.Left;

    public ITargetBlock<FlowMessage<FlowValue>> Right => _inner.Right;

    public ISourceBlock<FlowMessage<FlowResult<FlowJoinOutcome<FlowValue, FlowValue>>>> Output
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
