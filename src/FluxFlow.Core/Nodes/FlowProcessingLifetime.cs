using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Core.Nodes;

// Coordinates termination of supplied blocks. Components construct their own ports.
internal sealed class FlowProcessingLifetime
{
    private readonly CancellationTokenSource _stopping = new();
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<IDataflowBlock> _outputs;
    private readonly List<Func<ValueTask>> _releaseOutputs;
    private readonly List<IDataflowBlock> _inputs = [];
    private readonly List<IDataflowBlock> _terminalInputs = [];
    private readonly IDataflowBlock _processing;
    private readonly IDataflowBlock _events;
    private readonly object _component;
    private Exception? _failure;
    private int _outputShutdownStarted;
    private int _disposed;
    private readonly CancellationTokenRegistration _cancellationRegistration;

    public CancellationToken Stopping => _stopping.Token;
    public Task Completion => _completion.Task;
    internal IEnumerable<IDataflowBlock> TerminalInputs => _terminalInputs;

    public FlowProcessingLifetime(IDataflowBlock input, FlowOutput<Messaging.FlowMessage> output,
        IDataflowBlock events, object component, Func<ValueTask>? flush = null,
        Func<CancellationToken, Task>? drain = null,
        CancellationToken cancellationToken = default)
    {
        _processing = input;
        _outputs = [output];
        _releaseOutputs = [output.DisposeAsync];
        _events = events;
        _component = component;
        _cancellationRegistration = cancellationToken.Register(() => Fault(new OperationCanceledException(cancellationToken)));
        _ = ObserveOutputAsync(output);
        _ = CompleteWhenDrainedAsync(flush, drain is null ? Task.CompletedTask : DrainAsync(drain));
    }

    public void AddOutput<T>(FlowOutput<T> output)
    {
        _outputs.Add(output);
        _releaseOutputs.Add(output.DisposeAsync);
        _ = ObserveOutputAsync(output);
    }

    public void AddInput(IDataflowBlock input)
    {
        _inputs.Add(input);
        _ = ObserveProcessingAsync(input);
        if (_failure is { } failure) input.Fault(failure);
    }

    public void Complete()
    {
        _processing.Complete();
        foreach (var input in _inputs) input.Complete();
    }

    internal void AddTerminalInput(IDataflowBlock input)
    {
        _terminalInputs.Add(input);
        _ = ObserveProcessingAsync(input);
        if (_failure is { } failure) input.Fault(failure);
    }

    public void Fault(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (Completion.IsCompleted || Interlocked.CompareExchange(ref _failure, exception, null) is not null)
            return;

        FlowDiagnostics.LifecycleException(_component, "node", exception);

        Interlocked.Exchange(ref _outputShutdownStarted, 1);
        try { _stopping.Cancel(); }
        catch (AggregateException cancellationFailure)
        {
            FlowDiagnostics.LifecycleException(_component, "node.stop", cancellationFailure);
            // A component's cancellation callback must not strand the remaining blocks.
            exception = new AggregateException("Processing and cancellation failed.", exception, cancellationFailure);
            Volatile.Write(ref _failure, exception);
        }
        _processing.Fault(exception);
        foreach (var input in _inputs) input.Fault(exception);
        foreach (var input in _terminalInputs) input.Fault(exception);
        foreach (var output in _outputs) output.Fault(exception);
        _events.Complete();
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
            FlowDiagnostics.LifecycleException(_component, "node.dispose", error);
            throw;
        }
        finally
        {
            try
            {
                foreach (var releaseOutput in _releaseOutputs)
                    await releaseOutput().ConfigureAwait(false);
            }
            finally
            {
                _cancellationRegistration.Dispose();
                try { _stopping.Cancel(); }
                catch (Exception error)
                {
                    FlowDiagnostics.LifecycleException(_component, "node.stop", error);
                    throw;
                }
                _stopping.Dispose();
            }
        }
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

    private async Task DrainAsync(Func<CancellationToken, Task> drain)
    {
        try { await drain(Stopping).ConfigureAwait(false); }
        catch (Exception error) { Fault(Unwrap(error)); }
    }

    private async Task CompleteWhenDrainedAsync(Func<ValueTask>? flush, Task drain)
    {
        try
        {
            await Task.WhenAll(ObserveProcessingAsync(_processing), drain).ConfigureAwait(false);
            foreach (var input in _inputs) input.Complete();
            await Task.WhenAll(_inputs.Select(static input => input.Completion)).ConfigureAwait(false);
            if (_failure is null)
            {
                if (flush is not null) await flush().ConfigureAwait(false);
                Interlocked.Exchange(ref _outputShutdownStarted, 1);
                foreach (var output in _outputs) output.Complete();
                _events.Complete();
            }
        }
        catch (Exception error) { Fault(Unwrap(error)); }

        try
        {
            await Task.WhenAll(_outputs.Select(static output => output.Completion)
                .Append(_events.Completion)).ConfigureAwait(false);
        }
        catch (Exception error) { Fault(Unwrap(error)); }

        // Feedback such as acknowledgements stays available until data outputs drain.
        foreach (var input in _terminalInputs) input.Complete();
        try
        {
            await Task.WhenAll(_terminalInputs.Select(static input => input.Completion)).ConfigureAwait(false);
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
