using System.Threading.Tasks.Dataflow;
using FluxFlow.Nodes;

namespace FluxFlow.Components.Timers.Nodes;

internal sealed class TimerResultPipeline<TMessage> : IFlowNode
{
    private readonly BufferBlock<TMessage> _input;
    private readonly ActionBlock<TMessage> _processor;
    private readonly FlowOutput<TMessage> _output;
    private readonly BroadcastBlock<FlowMessage> _events = new(static @event => @event);
    private readonly Func<ValueTask>? _onInputCompleted;
    private readonly Func<ValueTask>? _onDispose;
    private readonly CancellationTokenSource _stopping = new();
    private readonly TaskCompletionSource _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _outputShutdownStarted;
    private int _disposed;

    public TimerResultPipeline(
        int boundedCapacity,
        Func<TMessage, bool> isError,
        Func<TMessage, Task> process,
        Func<ValueTask>? onInputCompleted = null,
        Func<ValueTask>? onDispose = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(boundedCapacity, 1);
        ArgumentNullException.ThrowIfNull(isError);
        ArgumentNullException.ThrowIfNull(process);

        _onInputCompleted = onInputCompleted;
        _onDispose = onDispose;
        _output = new FlowOutput<TMessage>(
            new FlowOutputOptions { Capacity = boundedCapacity });
        _input = new BufferBlock<TMessage>(
            new DataflowBlockOptions { BoundedCapacity = boundedCapacity });
        _processor = new ActionBlock<TMessage>(
            async message =>
            {
                if (isError(message))
                {
                    await EmitAsync(message, _stopping.Token).ConfigureAwait(false);
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
        _ = ObserveOutputTerminationAsync();
        _ = MonitorCompletionAsync();
    }

    public ITargetBlock<TMessage> Input => _input;

    public ISourceBlock<TMessage> Output => _output;

    public ISourceBlock<FlowMessage> Events => _events;

    public Task Completion => _completion.Task;

    public CancellationToken Stopping => _stopping.Token;

    public async ValueTask EmitAsync(
        TMessage message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (await _output.SendAsync(message, cancellationToken).ConfigureAwait(false))
            return;

        try
        {
            await _output.Completion.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            throw Unwrap(exception);
        }

        throw new InvalidOperationException("Timer output is no longer accepting data.");
    }

    public bool PublishEvent(FlowEvent @event)
    {
        ArgumentNullException.ThrowIfNull(@event);
        return _events.Post(@event);
    }

    public void Complete() => _input.Complete();

    public void Fault(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        Interlocked.Exchange(ref _outputShutdownStarted, 1);
        _stopping.Cancel();
        ((IDataflowBlock)_input).Fault(exception);
        ((IDataflowBlock)_processor).Fault(exception);
        _output.Fault(exception);
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
            try
            {
                await _output.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                _stopping.Cancel();
                _stopping.Dispose();
            }
        }
    }

    private async Task MonitorCompletionAsync()
    {
        try
        {
            await _processor.Completion.ConfigureAwait(false);
            if (_onInputCompleted is not null)
                await _onInputCompleted().ConfigureAwait(false);

            Interlocked.Exchange(ref _outputShutdownStarted, 1);
            _output.Complete();
            _events.Complete();
            await Task.WhenAll(_output.Completion, _events.Completion).ConfigureAwait(false);
            _completion.TrySetResult();
        }
        catch (Exception exception)
        {
            Fault(Unwrap(exception));
        }
    }

    private async Task ObserveOutputTerminationAsync()
    {
        try
        {
            await _output.Completion.ConfigureAwait(false);
            if (Volatile.Read(ref _outputShutdownStarted) == 0 &&
                !_processor.Completion.IsCompleted)
            {
                Fault(new InvalidOperationException(
                    "Timer output completed before input processing stopped."));
            }
        }
        catch (Exception exception)
        {
            if (!_completion.Task.IsCompleted)
                Fault(Unwrap(exception));
        }
    }

    private static Exception Unwrap(Exception exception)
        => exception is AggregateException aggregate && aggregate.InnerExceptions.Count == 1
            ? aggregate.InnerException!
            : exception;
}
