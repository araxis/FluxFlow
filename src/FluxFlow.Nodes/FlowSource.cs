using System.Threading.Tasks.Dataflow;
using FluxFlow.Data;

namespace FluxFlow.Nodes;

/// <summary>
/// Base for a zero-input source. Normal data outputs are bounded and reliable;
/// events remain a best-effort observation stream.
/// </summary>
public abstract class FlowSource<TOutput> : IFlowSource
{
    private readonly FlowOutput<FlowMessage<TOutput>> _output;
    private readonly BroadcastBlock<FlowEvent> _events;
    private readonly List<IDataflowBlock> _extraOutputs = [];
    private readonly CancellationTokenSource _stopping = new();
    private readonly TaskCompletionSource _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly int _outputCapacity;
    private Exception? _outputFailure;
    private int _outputShutdownStarted;
    private int _started;
    private int _disposed;

    protected FlowSource(FlowSourceOptions? options = null)
    {
        options ??= new FlowSourceOptions();
        if (options.OutputCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "OutputCapacity must be greater than zero.");
        }

        _outputCapacity = options.OutputCapacity;
        _output = CreateOutput<FlowMessage<TOutput>>();
        _events = new BroadcastBlock<FlowEvent>(static value => value);
        _ = ObserveOutputTerminationAsync(_output);
    }

    public ISourceBlock<FlowMessage<TOutput>> Output => _output;

    public ISourceBlock<FlowEvent> Events => _events;

    public Task Completion => _completion.Task;

    /// <summary>Signaled when the source is asked to stop.</summary>
    protected CancellationToken Stopping => _stopping.Token;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled(cancellationToken);
        }

        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            return Task.CompletedTask;
        }

        _ = ProduceAsync();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Produce messages via <see cref="EmitAsync"/> until this returns or
    /// <paramref name="cancellationToken"/> is canceled.
    /// </summary>
    protected abstract Task RunAsync(CancellationToken cancellationToken);

    protected async ValueTask EmitAsync(
        FlowMessage<TOutput> message,
        CancellationToken cancellationToken = default)
    {
        if (await _output.SendAsync(message, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        await ThrowOutputUnavailableAsync(_output).ConfigureAwait(false);
    }

    /// <summary>
    /// Reliably accepts a value on an additional output created by <see cref="AddOutput{T}"/>.
    /// </summary>
    protected static async ValueTask EmitAsync<T>(
        FlowOutput<T> output,
        T value,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (await output.SendAsync(value, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        await ThrowOutputUnavailableAsync(output).ConfigureAwait(false);
    }

    /// <summary>Creates an additional bounded reliable data output owned by this source.</summary>
    protected FlowOutput<T> AddOutput<T>()
    {
        var output = CreateOutput<T>();
        _extraOutputs.Add(output);
        _ = ObserveOutputTerminationAsync(output);
        return output;
    }

    protected ValueTask EmitErrorAsync(
        FlowError error,
        CancellationToken cancellationToken = default)
        => EmitAsync(FlowMessage.CreateError<TOutput>(error), cancellationToken);

    protected bool EmitEvent(FlowEvent @event) => _events.Post(@event);

    public void Complete() => _stopping.Cancel();

    public void Fault(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        Interlocked.Exchange(ref _outputShutdownStarted, 1);
        _stopping.Cancel();
        FaultOutputs(exception);
        _events.Complete();
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
            try
            {
                Interlocked.Exchange(ref _outputShutdownStarted, 1);
                await CompleteAndAwaitOutputsAsync().ConfigureAwait(false);
                _completion.TrySetResult();
            }
            catch (Exception exception)
            {
                var unwrapped = Unwrap(exception);
                Interlocked.Exchange(ref _outputShutdownStarted, 1);
                FaultOutputs(unwrapped);
                _events.Complete();
                _completion.TrySetException(unwrapped);
            }
        }

        try
        {
            await Completion.ConfigureAwait(false);
        }
        catch
        {
            // Completion remains the authoritative fault surface.
        }

        try
        {
            await OnDisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            try
            {
                await DisposeOutputsAsync().ConfigureAwait(false);
            }
            finally
            {
                _stopping.Dispose();
            }
        }
    }

    protected virtual ValueTask OnDisposeAsync() => ValueTask.CompletedTask;

    private FlowOutput<T> CreateOutput<T>()
        => new(new FlowOutputOptions { Capacity = _outputCapacity });

    private async Task ProduceAsync()
    {
        try
        {
            await RunAsync(_stopping.Token).ConfigureAwait(false);
            ThrowIfOutputFailed();
            Interlocked.Exchange(ref _outputShutdownStarted, 1);
            await CompleteAndAwaitOutputsAsync().ConfigureAwait(false);
            _completion.TrySetResult();
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
            try
            {
                ThrowIfOutputFailed();
                Interlocked.Exchange(ref _outputShutdownStarted, 1);
                await CompleteAndAwaitOutputsAsync().ConfigureAwait(false);
                _completion.TrySetResult();
            }
            catch (Exception exception)
            {
                var unwrapped = Unwrap(exception);
                Interlocked.Exchange(ref _outputShutdownStarted, 1);
                FaultOutputs(unwrapped);
                _events.Complete();
                _completion.TrySetException(unwrapped);
            }
        }
        catch (Exception exception)
        {
            var unwrapped = Unwrap(exception);
            Interlocked.Exchange(ref _outputShutdownStarted, 1);
            FaultOutputs(unwrapped);
            _events.Complete();
            _completion.TrySetException(unwrapped);
        }
    }

    private async Task ObserveOutputTerminationAsync(IDataflowBlock output)
    {
        try
        {
            await output.Completion.ConfigureAwait(false);
            if (Volatile.Read(ref _outputShutdownStarted) == 0 &&
                !_completion.Task.IsCompleted)
            {
                RecordOutputFailure(new InvalidOperationException(
                    "A source data output completed before source production stopped."));
            }
        }
        catch (Exception exception)
        {
            RecordOutputFailure(Unwrap(exception));
        }
    }

    private void RecordOutputFailure(Exception exception)
    {
        if (_completion.Task.IsCompleted)
        {
            return;
        }

        Interlocked.CompareExchange(ref _outputFailure, exception, null);
        _stopping.Cancel();
    }

    private void ThrowIfOutputFailed()
    {
        var failure = Volatile.Read(ref _outputFailure);
        if (failure is not null)
        {
            throw failure;
        }
    }

    private async Task CompleteAndAwaitOutputsAsync()
    {
        CompleteOutputs();
        _events.Complete();

        var completions = new List<Task> { _output.Completion, _events.Completion };
        completions.AddRange(_extraOutputs.Select(static output => output.Completion));
        await Task.WhenAll(completions).ConfigureAwait(false);
    }

    private void CompleteOutputs()
    {
        _output.Complete();
        foreach (var output in _extraOutputs)
        {
            output.Complete();
        }
    }

    private void FaultOutputs(Exception exception)
    {
        _output.Fault(exception);
        foreach (var output in _extraOutputs)
        {
            output.Fault(exception);
        }
    }

    private async ValueTask DisposeOutputsAsync()
    {
        await _output.DisposeAsync().ConfigureAwait(false);
        foreach (var output in _extraOutputs.Cast<IAsyncDisposable>())
        {
            await output.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static async ValueTask ThrowOutputUnavailableAsync(IDataflowBlock output)
    {
        if (output.Completion.IsFaulted)
        {
            await output.Completion.ConfigureAwait(false);
        }

        throw new InvalidOperationException("The source data output is no longer accepting messages.");
    }

    private static Exception Unwrap(Exception exception)
        => exception is AggregateException aggregate
            ? aggregate.GetBaseException()
            : exception;
}
