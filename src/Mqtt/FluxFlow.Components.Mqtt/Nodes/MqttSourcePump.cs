using FluxFlow.Nodes;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.Mqtt.Nodes;

internal sealed class MqttSourcePump<T> : IAsyncDisposable
{
    private readonly BroadcastBlock<FlowMessage<T>> _output;
    private readonly BroadcastBlock<FlowEvent> _events = new(static @event => @event);
    private readonly Func<CancellationToken, Task> _run;
    private readonly Func<ValueTask> _dispose;
    private readonly CancellationTokenSource _stopping = new();
    private readonly TaskCompletionSource _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _started;
    private int _disposed;

    public MqttSourcePump(
        int outputCapacity,
        Func<CancellationToken, Task> run,
        Func<ValueTask> dispose)
    {
        if (outputCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(outputCapacity));
        _run = run ?? throw new ArgumentNullException(nameof(run));
        _dispose = dispose ?? throw new ArgumentNullException(nameof(dispose));
        _output = new BroadcastBlock<FlowMessage<T>>(
            static message => message,
            new DataflowBlockOptions { BoundedCapacity = outputCapacity });
    }

    public ISourceBlock<FlowMessage<T>> Output => _output;

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
        FlowMessage<T> message,
        CancellationToken cancellationToken)
        => _output.SendAsync(message, cancellationToken);

    public bool EmitEvent(FlowEvent @event) => _events.Post(@event);

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
        }

        try
        {
            await _dispose().ConfigureAwait(false);
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
