using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Nodes;

/// <summary>
/// Base for a source node: zero inputs. Once <see cref="StartAsync"/> is called it
/// produces <see cref="FlowMessage{T}"/> on <see cref="Output"/> (plus
/// <see cref="Errors"/>/<see cref="Events"/>) by running <see cref="RunAsync"/> — a loop,
/// a timer, or a watcher. Use for timers, file watchers, generators, and triggers.
/// <see cref="RunAsync"/> emits until it returns (source complete) or <see cref="Stopping"/>
/// is signalled (by <see cref="Complete"/>/dispose). No engine required.
/// </summary>
public abstract class FlowSource<TOutput> : IFlowSource
{
    private readonly BroadcastBlock<FlowMessage<TOutput>> _output = new(static message => message);
    private readonly BroadcastBlock<FlowError> _errors = new(static value => value);
    private readonly BroadcastBlock<FlowEvent> _events = new(static value => value);
    private readonly List<IDataflowBlock> _extraOutputs = new();
    private readonly CancellationTokenSource _stopping = new();
    private readonly TaskCompletionSource _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _started;
    private int _disposed;

    public ISourceBlock<FlowMessage<TOutput>> Output => _output;

    public ISourceBlock<FlowError> Errors => _errors;

    public ISourceBlock<FlowEvent> Events => _events;

    public Task Completion => _completion.Task;

    /// <summary>Signalled when the source is asked to stop (<see cref="Complete"/>/dispose).</summary>
    protected CancellationToken Stopping => _stopping.Token;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            return Task.CompletedTask;
        }

        _ = ProduceAsync();
        return Task.CompletedTask;
    }

    /// <summary>Produce messages via <see cref="Emit"/> until this returns or <paramref name="cancellationToken"/> fires.</summary>
    protected abstract Task RunAsync(CancellationToken cancellationToken);

    protected bool Emit(FlowMessage<TOutput> message) => _output.Post(message);

    /// <summary>Additional broadcast output port, completed/faulted with the source (see FlowNode.AddOutput).</summary>
    protected BroadcastBlock<T> AddOutput<T>()
    {
        var port = new BroadcastBlock<T>(static message => message);
        _extraOutputs.Add(port);
        return port;
    }

    protected bool EmitError(FlowError error) => _errors.Post(error);

    protected bool EmitEvent(FlowEvent @event) => _events.Post(@event);

    public void Complete() => _stopping.Cancel();

    public void Fault(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        _stopping.Cancel();
        FaultOutputs(exception);
        _completion.TrySetException(exception);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _stopping.Cancel();
        if (Volatile.Read(ref _started) == 0)
        {
            // Never started: nothing is producing, so settle completion ourselves.
            CompleteOutputs();
            _completion.TrySetResult();
        }

        try
        {
            await Completion.ConfigureAwait(false);
        }
        catch
        {
            // Completion may surface a fault; teardown must still run.
        }

        try
        {
            await OnDisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            _stopping.Dispose();
        }
    }

    protected virtual ValueTask OnDisposeAsync() => ValueTask.CompletedTask;

    private async Task ProduceAsync()
    {
        try
        {
            await RunAsync(_stopping.Token).ConfigureAwait(false);
            await CompleteAndAwaitOutputsAsync().ConfigureAwait(false);
            _completion.TrySetResult();
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
            await CompleteAndAwaitOutputsAsync().ConfigureAwait(false);
            _completion.TrySetResult();
        }
        catch (Exception exception)
        {
            FaultOutputs(exception);
            _completion.TrySetException(exception);
        }
    }

    private async Task CompleteAndAwaitOutputsAsync()
    {
        CompleteOutputs();
        var completions = new List<Task> { _output.Completion, _errors.Completion, _events.Completion };
        completions.AddRange(_extraOutputs.Select(extra => extra.Completion));
        await Task.WhenAll(completions).ConfigureAwait(false);
    }

    private void CompleteOutputs()
    {
        _output.Complete();
        _errors.Complete();
        _events.Complete();
        foreach (var extra in _extraOutputs)
        {
            extra.Complete();
        }
    }

    private void FaultOutputs(Exception exception)
    {
        ((IDataflowBlock)_output).Fault(exception);
        foreach (var extra in _extraOutputs)
        {
            extra.Fault(exception);
        }

        // Errors/Events carry the diagnostics that explain the fault. Complete (flush)
        // them rather than Fault them: faulting a BroadcastBlock discards its buffered
        // message, which would drop the very FlowError a consumer needs to see. The
        // authoritative fault is surfaced on Completion by the caller.
        _errors.Complete();
        _events.Complete();
    }
}
