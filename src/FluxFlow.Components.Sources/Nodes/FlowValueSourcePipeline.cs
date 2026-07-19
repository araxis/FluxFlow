using System.Threading.Tasks.Dataflow;
using FluxFlow.Data;
using FluxFlow.Nodes;

namespace FluxFlow.Components.Sources.Nodes;

internal sealed class FlowValueSourcePipeline : IAsyncDisposable
{
    private readonly BroadcastBlock<FlowMessage<FlowValue>> _output;
    private readonly BroadcastBlock<FlowEvent> _events = new(static @event => @event);
    private readonly Func<CancellationToken, Task> _run;
    private readonly CancellationTokenSource _stopping = new();
    private readonly TaskCompletionSource _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _started;
    private int _disposed;

    public FlowValueSourcePipeline(
        int outputCapacity,
        Func<CancellationToken, Task> run)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(outputCapacity, 1);
        ArgumentNullException.ThrowIfNull(run);

        _run = run;
        _output = new BroadcastBlock<FlowMessage<FlowValue>>(
            static message => message,
            new DataflowBlockOptions { BoundedCapacity = outputCapacity });
    }

    public ISourceBlock<FlowMessage<FlowValue>> Output => _output;

    public ISourceBlock<FlowEvent> Events => _events;

    public Task Completion => _completion.Task;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled(cancellationToken);
        if (Interlocked.Exchange(ref _started, 1) != 0)
            return Task.CompletedTask;

        _ = RunAsync();
        return Task.CompletedTask;
    }

    public Task<bool> EmitAsync(
        FlowMessage<FlowValue> message,
        CancellationToken cancellationToken)
        => _output.SendAsync(message, cancellationToken);

    public bool PublishEvent(FlowEvent @event)
    {
        ArgumentNullException.ThrowIfNull(@event);
        return _events.Post(@event);
    }

    public void Complete() => _stopping.Cancel();

    public void Fault(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        _stopping.Cancel();
        ((IDataflowBlock)_output).Fault(exception);
        _events.Complete();
        _completion.TrySetException(exception);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        Complete();
        if (Volatile.Read(ref _started) == 0)
        {
            _output.Complete();
            _events.Complete();
            _completion.TrySetResult();
        }

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
            _stopping.Dispose();
        }
    }

    private async Task RunAsync()
    {
        try
        {
            await _run(_stopping.Token).ConfigureAwait(false);
            await CompleteOutputsAsync().ConfigureAwait(false);
            _completion.TrySetResult();
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
            await CompleteOutputsAsync().ConfigureAwait(false);
            _completion.TrySetResult();
        }
        catch (Exception exception)
        {
            ((IDataflowBlock)_output).Fault(exception);
            _events.Complete();
            _completion.TrySetException(exception);
        }
    }

    private async Task CompleteOutputsAsync()
    {
        _output.Complete();
        _events.Complete();
        await Task.WhenAll(_output.Completion, _events.Completion).ConfigureAwait(false);
    }
}
