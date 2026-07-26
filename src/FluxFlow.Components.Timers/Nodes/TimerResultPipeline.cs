using System.Threading.Tasks.Dataflow;
using FluxFlow.Nodes;

namespace FluxFlow.Components.Timers.Nodes;

internal sealed class TimerResultPipeline<T> : IFlowNode
{
    private readonly BufferBlock<FlowMessage<T>> _input;
    private readonly ActionBlock<FlowMessage<T>> _processor;
    private readonly BroadcastBlock<FlowMessage<T>> _output =
        new(static message => message);
    private readonly BroadcastBlock<FlowEvent> _events = new(static @event => @event);
    private readonly Func<ValueTask>? _onInputCompleted;
    private readonly Func<ValueTask>? _onDispose;
    private readonly CancellationTokenSource _stopping = new();
    private readonly TaskCompletionSource _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _disposed;

    public TimerResultPipeline(
        int boundedCapacity,
        Func<FlowMessage<T>, Task> process,
        Func<ValueTask>? onInputCompleted = null,
        Func<ValueTask>? onDispose = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(boundedCapacity, 1);
        ArgumentNullException.ThrowIfNull(process);

        _onInputCompleted = onInputCompleted;
        _onDispose = onDispose;
        _input = new BufferBlock<FlowMessage<T>>(
            new DataflowBlockOptions { BoundedCapacity = boundedCapacity });
        _processor = new ActionBlock<FlowMessage<T>>(
            async message =>
            {
                if (message.IsError)
                {
                    Emit(message);
                    return;
                }

                await process(message).ConfigureAwait(false);
            },
            new ExecutionDataflowBlockOptions
            {
                BoundedCapacity = 1,
                MaxDegreeOfParallelism = 1,
                EnsureOrdered = true
            });
        _input.LinkTo(_processor, new DataflowLinkOptions { PropagateCompletion = true });
        _ = MonitorCompletionAsync();
    }

    public ITargetBlock<FlowMessage<T>> Input => _input;

    public ISourceBlock<FlowMessage<T>> Output => _output;

    public ISourceBlock<FlowEvent> Events => _events;

    public Task Completion => _completion.Task;

    public CancellationToken Stopping => _stopping.Token;

    public bool Emit(FlowMessage<T> message) => _output.Post(message);

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
