using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Routing.Contracts;
using FluxFlow.Components.Routing.Options;
using FluxFlow.Data;
using FluxFlow.Nodes;

namespace FluxFlow.Components.Routing.Nodes;

/// <summary>Canonical FlowValue window with one normal result output.</summary>
public sealed class FlowValueWindowNode : IFlowNode
{
    private readonly WindowNodeRuntime<FlowValue> _inner;
    private readonly BroadcastBlock<FlowMessage<FlowResult<FlowWindow<FlowValue>>>> _output =
        new(static message => message);
    private readonly TaskCompletionSource _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly IReadOnlyCollection<IDataflowBlock> _adapters;
    private int _disposed;

    public FlowValueWindowNode(
        WindowRoutingOptions options,
        TimeProvider? clock = null)
    {
        _inner = new WindowNodeRuntime<FlowValue>(options, clock);
        _adapters =
        [
            RoutingFlowResultAdapter.LinkSuccess(
                _inner.Output,
                _output,
                static window => window,
                static window => window.Reason switch
                {
                    FlowWindowEmitReason.Count => RoutingResultKinds.WindowCount,
                    FlowWindowEmitReason.Time => RoutingResultKinds.WindowTime,
                    _ => RoutingResultKinds.WindowCompleted
                },
                static window => window.EmittedAt),
            RoutingFlowResultAdapter.LinkErrors<FlowWindow<FlowValue>>(
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

    public ISourceBlock<FlowMessage<FlowResult<FlowWindow<FlowValue>>>> Output => _output;

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
