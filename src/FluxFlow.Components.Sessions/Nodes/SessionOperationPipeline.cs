using System.Threading.Tasks.Dataflow;
using FluxFlow.Data;
using FluxFlow.Nodes;

namespace FluxFlow.Components.Sessions.Nodes;

internal sealed class SessionOperationPipeline<TInput, TOutput> : IAsyncDisposable
{
    private readonly CancellationTokenSource _stopping = new();
    private readonly TransformBlock<FlowMessage<TInput>, FlowMessage<FlowResult<TOutput>>> _processor;
    private readonly BroadcastBlock<FlowMessage<FlowResult<TOutput>>> _output =
        new(static message => message);
    private readonly BroadcastBlock<FlowEvent> _events = new(static @event => @event);
    private readonly Func<Task>? _finalize;
    private readonly TaskCompletionSource _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _disposed;

    public SessionOperationPipeline(
        int boundedCapacity,
        Func<FlowMessage<TInput>, CancellationToken, Task<FlowMessage<FlowResult<TOutput>>>> process,
        Func<Task>? finalize = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(boundedCapacity, 1);
        ArgumentNullException.ThrowIfNull(process);

        _finalize = finalize;
        _processor = new TransformBlock<FlowMessage<TInput>, FlowMessage<FlowResult<TOutput>>>(
            message => process(message, _stopping.Token),
            new ExecutionDataflowBlockOptions
            {
                BoundedCapacity = boundedCapacity,
                EnsureOrdered = true,
                MaxDegreeOfParallelism = 1
            });
        _processor.LinkTo(_output, new DataflowLinkOptions { PropagateCompletion = true });
        _ = MonitorCompletionAsync();
    }

    public ITargetBlock<FlowMessage<TInput>> Input => _processor;

    public ISourceBlock<FlowMessage<FlowResult<TOutput>>> Output => _output;

    public ISourceBlock<FlowEvent> Events => _events;

    public Task Completion => _completion.Task;

    public void PublishEvent(FlowEvent value)
    {
        ArgumentNullException.ThrowIfNull(value);
        _events.Post(value);
    }

    public void Complete() => _processor.Complete();

    public void Fault(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        _stopping.Cancel();
        ((IDataflowBlock)_processor).Fault(exception);
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
        finally
        {
            _stopping.Cancel();
            _stopping.Dispose();
        }
    }

    private async Task MonitorCompletionAsync()
    {
        Exception? failure = null;
        try
        {
            await _processor.Completion.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failure = exception;
            try
            {
                ((IDataflowBlock)_output).Fault(exception);
            }
            catch
            {
                // The output may already be terminal.
            }
        }

        try
        {
            await _output.Completion.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failure ??= exception;
        }

        try
        {
            if (_finalize is not null)
                await _finalize().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failure ??= exception;
        }

        _events.Complete();
        try
        {
            await _events.Completion.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failure ??= exception;
        }

        if (failure is null)
            _completion.TrySetResult();
        else
            _completion.TrySetException(failure);
    }
}
