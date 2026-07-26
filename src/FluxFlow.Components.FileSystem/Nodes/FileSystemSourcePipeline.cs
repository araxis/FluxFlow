using System.Threading.Tasks.Dataflow;
using FluxFlow.Nodes;

namespace FluxFlow.Components.FileSystem.Nodes;

internal sealed class FileSystemSourcePipeline<T> : IAsyncDisposable
{
    private readonly BroadcastBlock<FlowMessage<T>> _output;
    private readonly BroadcastBlock<FlowEvent> _events = new(static value => value);
    private readonly Func<CancellationToken, Task> _run;
    private readonly CancellationTokenSource _stopping = new();
    private readonly TaskCompletionSource _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _started;
    private int _disposed;

    public FileSystemSourcePipeline(
        int boundedCapacity,
        Func<CancellationToken, Task> run)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(boundedCapacity, 1);
        ArgumentNullException.ThrowIfNull(run);

        _run = run;
        _output = new BroadcastBlock<FlowMessage<T>>(
            static message => message,
            new DataflowBlockOptions { BoundedCapacity = boundedCapacity });
    }

    public ISourceBlock<FlowMessage<T>> Output => _output;

    public ISourceBlock<FlowEvent> Events => _events;

    public Task Completion => _completion.Task;

    public bool IsStopping => _stopping.IsCancellationRequested;

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
        FlowMessage<T> message,
        CancellationToken cancellationToken)
        => _output.SendAsync(message, cancellationToken);

    public bool TryEmit(FlowMessage<T> message) => _output.Post(message);

    public bool PublishEvent(FlowEvent value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return _events.Post(value);
    }

    public void Complete() => _stopping.Cancel();

    public void Fault(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        _stopping.Cancel();
        TryFault(_output, exception);
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
            if (_completion.Task.IsCompleted)
                return;

            await CompleteOutputsAsync().ConfigureAwait(false);
            _completion.TrySetResult();
        }
        catch (Exception exception)
        {
            TryFault(_output, exception);
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

    private static void TryFault(IDataflowBlock block, Exception exception)
    {
        try
        {
            block.Fault(exception);
        }
        catch (InvalidOperationException)
        {
            // The block is already terminal.
        }
    }
}
