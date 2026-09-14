using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Nodes;

// Owns termination and outputs for processing components. Native blocks own execution.
internal sealed class FlowProcessingLifetime(int outputCapacity, object component)
{
    private readonly CancellationTokenSource _stopping = new();
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<IDataflowBlock> _extraOutputs = [];
    private IDataflowBlock _processing = null!;
    private Task _drain = Task.CompletedTask;
    private Exception? _failure;
    private int _outputShutdownStarted;
    private int _disposed;

    public FlowOutput<FlowMessage> Output { get; } = new(new FlowOutputOptions { Capacity = outputCapacity });
    public BroadcastBlock<FlowMessage> Events { get; } = new(static message => message);
    public CancellationToken Stopping => _stopping.Token;
    public Task Completion => _completion.Task;

    // Called once by the wrapper constructor, before the component can accept input.
    public void Initialize(Func<ValueTask> flush, IDataflowBlock processing, Task? drain = null)
    {
        _processing = processing;
        _drain = drain ?? Task.CompletedTask;
        _ = ObserveOutputAsync(Output);
        _ = CompleteWhenDrainedAsync(flush);
    }

    public FlowOutput<T> AddOutput<T>()
    {
        var output = new FlowOutput<T>(new FlowOutputOptions { Capacity = outputCapacity });
        _extraOutputs.Add(output);
        _ = ObserveOutputAsync(output);
        return output;
    }

    public void Complete() => _processing.Complete();

    public void Fault(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (Completion.IsCompleted || Interlocked.CompareExchange(ref _failure, exception, null) is not null)
            return;

        FlowDiagnostics.LifecycleException(component, "node", exception);

        Interlocked.Exchange(ref _outputShutdownStarted, 1);
        try { _stopping.Cancel(); }
        catch (AggregateException cancellationFailure)
        {
            FlowDiagnostics.LifecycleException(component, "node.stop", cancellationFailure);
            // A component's cancellation callback must not strand the remaining blocks.
            exception = new AggregateException("Processing and cancellation failed.", exception, cancellationFailure);
            Volatile.Write(ref _failure, exception);
        }
        _processing.Fault(exception);
        Output.Fault(exception);
        foreach (var output in _extraOutputs) output.Fault(exception);
        Events.Complete();
    }

    public async ValueTask DisposeAsync(Func<ValueTask> release)
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Complete();
        try { await Completion.ConfigureAwait(false); }
        catch { /* Completion remains the authoritative processing-fault surface. */ }

        try { await release().ConfigureAwait(false); }
        catch (Exception error)
        {
            FlowDiagnostics.LifecycleException(component, "node.dispose", error);
            throw;
        }
        finally
        {
            try
            {
                await Output.DisposeAsync().ConfigureAwait(false);
                foreach (var output in _extraOutputs.Cast<IAsyncDisposable>())
                    await output.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                try { _stopping.Cancel(); }
                catch (Exception error)
                {
                    FlowDiagnostics.LifecycleException(component, "node.stop", error);
                    throw;
                }
                _stopping.Dispose();
            }
        }
    }

    public static async ValueTask EmitAsync<T>(FlowOutput<T> output, T value, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (await output.SendAsync(value, cancellationToken).ConfigureAwait(false)) return;
        if (output.Completion.IsFaulted) await output.Completion.ConfigureAwait(false);
        throw new InvalidOperationException("The node data output is no longer accepting messages.");
    }

    private async Task ObserveProcessingAsync(IDataflowBlock block)
    {
        try { await block.Completion.ConfigureAwait(false); }
        catch (Exception error) { Fault(Unwrap(error)); }
    }

    private async Task ObserveOutputAsync(IDataflowBlock output)
    {
        try
        {
            await output.Completion.ConfigureAwait(false);
            if (Volatile.Read(ref _outputShutdownStarted) == 0)
                Fault(new InvalidOperationException("A node data output completed before node processing stopped."));
        }
        catch (Exception error) { Fault(Unwrap(error)); }
    }

    private async Task CompleteWhenDrainedAsync(Func<ValueTask> flush)
    {
        try
        {
            await Task.WhenAll(ObserveProcessingAsync(_processing), _drain).ConfigureAwait(false);
            if (_failure is null)
            {
                await flush().ConfigureAwait(false);
                Interlocked.Exchange(ref _outputShutdownStarted, 1);
                Output.Complete();
                foreach (var output in _extraOutputs) output.Complete();
                Events.Complete();
            }
        }
        catch (Exception error) { Fault(Unwrap(error)); }

        try
        {
            await Task.WhenAll(new[] { Output.Completion, Events.Completion }
                .Concat(_extraOutputs.Select(static output => output.Completion))).ConfigureAwait(false);
        }
        catch (Exception error) { Fault(Unwrap(error)); }

        if (_failure is { } failure) _completion.TrySetException(failure);
        else _completion.TrySetResult();
    }

    private static Exception Unwrap(Exception error)
        => error is AggregateException { InnerExceptions.Count: 1 } aggregate
            ? Unwrap(aggregate.InnerException!)
            : error;
}
