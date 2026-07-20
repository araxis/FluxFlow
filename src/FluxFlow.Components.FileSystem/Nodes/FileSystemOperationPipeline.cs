using System.Threading.Tasks.Dataflow;
using FluxFlow.Data;
using FluxFlow.Nodes;

namespace FluxFlow.Components.FileSystem.Nodes;

internal sealed class FileSystemOperationPipeline<TInput, TOutput> : IAsyncDisposable
{
    private readonly CancellationTokenSource _stopping = new();
    private readonly TransformBlock<
        FlowMessage<TInput>,
        FlowMessage<FlowResult<TOutput>>> _processor;
    private readonly BroadcastBlock<FlowMessage<FlowResult<TOutput>>> _output =
        new(static message => message);
    private readonly BroadcastBlock<FlowEvent> _events = new(static @event => @event);
    private readonly TaskCompletionSource _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _disposed;

    public FileSystemOperationPipeline(
        int boundedCapacity,
        Func<
            FlowMessage<TInput>,
            CancellationToken,
            Task<FlowMessage<FlowResult<TOutput>>>> process)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(boundedCapacity, 1);
        ArgumentNullException.ThrowIfNull(process);

        _processor = new TransformBlock<
            FlowMessage<TInput>,
            FlowMessage<FlowResult<TOutput>>>(
                message => process(message, _stopping.Token),
                new ExecutionDataflowBlockOptions
                {
                    BoundedCapacity = boundedCapacity,
                    MaxDegreeOfParallelism = 1,
                    EnsureOrdered = true
                });
        _processor.LinkTo(_output, new DataflowLinkOptions { PropagateCompletion = true });
        _ = MonitorCompletionAsync();
    }

    public ITargetBlock<FlowMessage<TInput>> Input => _processor;

    public ISourceBlock<FlowMessage<FlowResult<TOutput>>> Output => _output;

    public ISourceBlock<FlowEvent> Events => _events;

    public Task Completion => _completion.Task;

    public void Complete() => _processor.Complete();

    public void Fault(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        _stopping.Cancel();
        ((IDataflowBlock)_processor).Fault(exception);
    }

    public void PublishEvent(FlowEvent @event)
    {
        ArgumentNullException.ThrowIfNull(@event);
        _events.Post(@event);
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
        try
        {
            await _processor.Completion.ConfigureAwait(false);
            await _output.Completion.ConfigureAwait(false);
            _events.Complete();
            await _events.Completion.ConfigureAwait(false);
            _completion.TrySetResult();
        }
        catch (Exception exception)
        {
            try
            {
                ((IDataflowBlock)_output).Fault(exception);
            }
            catch
            {
                // The output may already be terminal.
            }

            _events.Complete();
            _completion.TrySetException(exception);
        }
    }
}
