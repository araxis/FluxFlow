using System.Threading.Tasks.Dataflow;
using FluxFlow.Data;
using FluxFlow.Nodes;

namespace FluxFlow.Components.Timers.Nodes;

internal sealed class FlowValueTimerResultPipeline : IFlowNode
{
    private readonly BufferBlock<FlowMessage<FlowValue>> _input;
    private readonly ActionBlock<FlowMessage<FlowValue>> _processor;
    private readonly BroadcastBlock<FlowMessage<FlowResult<FlowValue>>> _output =
        new(static message => message);
    private readonly BroadcastBlock<FlowEvent> _events = new(static @event => @event);
    private readonly Func<ValueTask>? _onInputCompleted;
    private readonly Func<ValueTask>? _onDispose;
    private readonly CancellationTokenSource _stopping = new();
    private readonly TaskCompletionSource _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _disposed;

    public FlowValueTimerResultPipeline(
        int boundedCapacity,
        Func<FlowMessage<FlowValue>, Task> process,
        Func<ValueTask>? onInputCompleted = null,
        Func<ValueTask>? onDispose = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(boundedCapacity, 1);
        ArgumentNullException.ThrowIfNull(process);

        _onInputCompleted = onInputCompleted;
        _onDispose = onDispose;
        _input = new BufferBlock<FlowMessage<FlowValue>>(
            new DataflowBlockOptions { BoundedCapacity = boundedCapacity });
        _processor = new ActionBlock<FlowMessage<FlowValue>>(
            process,
            new ExecutionDataflowBlockOptions
            {
                BoundedCapacity = 1,
                MaxDegreeOfParallelism = 1,
                EnsureOrdered = true
            });
        _input.LinkTo(_processor, new DataflowLinkOptions { PropagateCompletion = true });
        _ = MonitorCompletionAsync();
    }

    public ITargetBlock<FlowMessage<FlowValue>> Input => _input;

    public ISourceBlock<FlowMessage<FlowResult<FlowValue>>> Output => _output;

    public ISourceBlock<FlowEvent> Events => _events;

    public Task Completion => _completion.Task;

    public CancellationToken Stopping => _stopping.Token;

    public bool Emit(FlowMessage<FlowResult<FlowValue>> message) => _output.Post(message);

    public bool PublishEvent(FlowEvent @event)
    {
        ArgumentNullException.ThrowIfNull(@event);
        return _events.Post(@event);
    }

    public void Complete() => _input.Complete();

    public void Fault(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        _stopping.Cancel();
        ((IDataflowBlock)_input).Fault(exception);
        ((IDataflowBlock)_processor).Fault(exception);
        ((IDataflowBlock)_output).Fault(exception);
        _events.Complete();
        _completion.TrySetException(exception);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        Complete();
        try
        {
            await Completion.ConfigureAwait(false);
        }
        catch
        {
            // Completion remains the authoritative unexpected-fault surface.
        }

        try
        {
            if (_onDispose is not null)
                await _onDispose().ConfigureAwait(false);
        }
        finally
        {
            _stopping.Cancel();
            _stopping.Dispose();
        }
    }

    private async Task MonitorCompletionAsync()
    {
        try
        {
            await _processor.Completion.ConfigureAwait(false);
            if (_onInputCompleted is not null)
                await _onInputCompleted().ConfigureAwait(false);

            _output.Complete();
            _events.Complete();
            await Task.WhenAll(_output.Completion, _events.Completion).ConfigureAwait(false);
            _completion.TrySetResult();
        }
        catch (Exception exception)
        {
            _stopping.Cancel();
            ((IDataflowBlock)_input).Fault(exception);
            ((IDataflowBlock)_output).Fault(exception);
            _events.Complete();
            _completion.TrySetException(exception);
        }
    }
}
