using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Messaging;

namespace FluxFlow.Core.Nodes;

/// <summary>Coordinates component-created blocks. Declare blocks, then call Observe once construction is complete.</summary>
public sealed class FlowLifetime
{
    private readonly object _gate = new();
    private readonly object _component;
    private readonly Func<CancellationToken, Task>? _produce;
    private readonly Func<ValueTask>? _flush;
    private readonly CancellationToken _cancellationToken;
    private readonly List<IDataflowBlock> _inputs = [];
    private readonly List<IDataflowBlock> _outputs = [];
    private readonly List<IDataflowBlock> _events = [];
    private readonly List<ITargetBlock<FlowMessage>> _terminalInputs = [];
    private readonly CancellationTokenSource _stopping = new();
    private readonly CancellationTokenSource _controlStopping = new();
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _productionCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private CancellationTokenRegistration _cancellationRegistration;
    private Task? _cancellation;
    private Task? _controlCancellation;
    private Exception? _failure;
    private bool _observing;
    private int _started;
    private int _outputShutdownStarted;
    private int _disposed;

    public FlowLifetime(object component, Func<ValueTask>? flush = null,
        Func<CancellationToken, Task>? produce = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(component);
        _component = component;
        _flush = flush;
        _produce = produce;
        _cancellationToken = cancellationToken;
    }

    public CancellationToken Stopping => _stopping.Token;
    public CancellationToken ControlStopping => _produce is null ? Stopping : _controlStopping.Token;
    public Task Completion => _completion.Task;
    public Task ProductionCompletion => _productionCompletion.Task;
    public IEnumerable<ITargetBlock<FlowMessage>> TerminalInputs => _terminalInputs.AsReadOnly();
    private string Operation => _produce is null ? "node" : "source";

    /// <summary>Declares an ordinary input. Completion of the first input begins draining all ordinary inputs.</summary>
    public TBlock HasInput<TBlock>(TBlock input) where TBlock : IDataflowBlock
        => Declare(input, _inputs);

    /// <summary>Declares a data output to complete after processing and flushing.</summary>
    public TBlock HasOutput<TBlock>(TBlock output) where TBlock : IDataflowBlock
        => Declare(output, _outputs);

    /// <summary>Declares an event output, drained rather than faulted when the component fails.</summary>
    public TBlock HasEvents<TBlock>(TBlock events) where TBlock : IDataflowBlock
        => Declare(events, _events);

    /// <summary>Declares feedback which must remain available until accepted business work has finished.</summary>
    public TBlock HasTerminalInput<TBlock>(TBlock input) where TBlock : ITargetBlock<FlowMessage>
        => Declare(input, _terminalInputs);

    private TBlock Declare<TBlock, TStored>(TBlock block, List<TStored> blocks) where TBlock : TStored
    {
        ArgumentNullException.ThrowIfNull(block);
        lock (_gate)
        {
            if (_observing) throw new InvalidOperationException("Declare blocks before observing their lifetime.");
            blocks.Add(block);
        }
        return block;
    }

    /// <summary>Seals declarations and observes native completion. This does not start a producer or construct any blocks.</summary>
    public void Observe()
    {
        lock (_gate)
        {
            if (_observing) throw new InvalidOperationException("The component lifetime is already being observed.");
            if (_inputs.Count == 0 && _produce is null)
                throw new InvalidOperationException("Declare an input or supply a producer before observing the lifetime.");
            _observing = true;
        }
        foreach (var output in _outputs) _ = ObserveOutputAsync(output);
        foreach (var input in _inputs) _ = ObserveCompletionAsync(input.Completion);
        foreach (var input in _terminalInputs) _ = ObserveCompletionAsync(input.Completion);
        _cancellationRegistration = _cancellationToken.Register(() => Fault(new OperationCanceledException(_cancellationToken)));
        if (_produce is null) StartLifetime(produce: false);
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        EnsureObserved();
        if (_produce is null) throw new InvalidOperationException("This component has no producer to start.");
        if (cancellationToken.IsCancellationRequested) return Task.FromCanceled(cancellationToken);
        if (Volatile.Read(ref _disposed) == 0) StartLifetime(produce: true);
        return Task.CompletedTask;
    }

    public void Complete()
    {
        EnsureObserved();
        foreach (var input in _inputs) input.Complete();
        if (_produce is not null)
        {
            foreach (var input in _terminalInputs) input.Complete();
            StopProduction();
        }
    }

    /// <summary>Stops production without closing replies needed by downstream work.</summary>
    public void StopProduction()
    {
        EnsureObserved();
        if (_produce is null) { Complete(); return; }
        _ = RequestStop();
        StartLifetime(produce: false);
    }

    public void Fault(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        EnsureObserved();
        if (!RecordFailure(exception)) return;
        Interlocked.Exchange(ref _outputShutdownStarted, 1);
        if (_produce is null)
        {
            try { _stopping.Cancel(); }
            catch (AggregateException cancellationFailure)
            {
                FlowDiagnostics.LifecycleException(_component, "node.stop", cancellationFailure);
                exception = new AggregateException("Processing and cancellation failed.", exception, cancellationFailure);
                lock (_gate) _failure = exception;
            }
        }
        else _ = RequestStop();
        foreach (var input in _inputs) input.Fault(exception);
        FaultControls(exception);
        foreach (var output in _outputs) output.Fault(exception);
        foreach (var events in _events) events.Complete();
        StartLifetime(produce: false);
    }

    public async ValueTask DisposeAsync(Func<ValueTask>? release = null)
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Complete();
        try { await Completion.ConfigureAwait(false); }
        catch { /* Completion remains the authoritative fault surface. */ }
        try { if (release is not null) await release().ConfigureAwait(false); }
        catch (Exception error)
        {
            FlowDiagnostics.LifecycleException(_component, Operation + ".dispose", error);
            throw;
        }
        finally
        {
            _cancellationRegistration.Dispose();
            try
            {
                if (_produce is null) _stopping.Cancel();
            }
            catch (Exception error)
            {
                FlowDiagnostics.LifecycleException(_component, "node.stop", error);
                throw;
            }
            finally { _stopping.Dispose(); _controlStopping.Dispose(); }
        }
    }

    private void EnsureObserved()
    {
        lock (_gate)
            if (!_observing) throw new InvalidOperationException("Observe the declared blocks before using their lifetime.");
    }

    private void StartLifetime(bool produce)
    {
        if (Interlocked.CompareExchange(ref _started, 1, 0) == 0) _ = RunLifetimeAsync(produce);
    }

    private async Task RunLifetimeAsync(bool produce)
    {
        try
        {
            if (_produce is not null)
            {
                try
                {
                    if (produce)
                    {
                        Stopping.ThrowIfCancellationRequested();
                        await _produce(Stopping).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (_stopping.IsCancellationRequested) { }
                catch (Exception error) { Fault(Unwrap(error)); }
                Interlocked.Exchange(ref _outputShutdownStarted, 1);
                try { await RequestStop().ConfigureAwait(false); }
                catch (Exception error)
                {
                    FlowDiagnostics.LifecycleException(_component, "source.stop", error);
                    lock (_gate) _failure = _failure is null ? Unwrap(error) : new AggregateException(_failure, error);
                }
            }
            else await ObserveCompletionAsync(_inputs[0].Completion).ConfigureAwait(false);

            foreach (var input in _inputs) input.Complete();
            await Task.WhenAll(_inputs.Select(static input => input.Completion)).ConfigureAwait(false);
            if (_failure is null && _flush is not null) await _flush().ConfigureAwait(false);
            Interlocked.Exchange(ref _outputShutdownStarted, 1);
            foreach (var output in _outputs)
            {
                if (_failure is { } failure) output.Fault(failure);
                else output.Complete();
            }
        }
        catch (Exception error) { Fault(Unwrap(error)); }

        if (_produce is null) foreach (var events in _events) events.Complete();
        await ObserveCompletionAsync(Task.WhenAll(_outputs.Select(static output => output.Completion))).ConfigureAwait(false);
        if (_failure is { } productionFailure) FaultControls(productionFailure);
        lock (_gate)
        {
            if (_failure is null) _productionCompletion.TrySetResult();
            else { _productionCompletion.TrySetException(_failure); _ = _productionCompletion.Task.Exception; }
        }

        if (_produce is null) foreach (var input in _terminalInputs) input.Complete();
        await ObserveCompletionAsync(Task.WhenAll(_terminalInputs.Select(static input => input.Completion))).ConfigureAwait(false);
        if (_produce is not null)
        {
            try { await CancelControlsAsync().ConfigureAwait(false); }
            catch (Exception error) { RecordFailure(Unwrap(error)); }
        }
        foreach (var events in _events) events.Complete();
        await ObserveCompletionAsync(Task.WhenAll(_events.Select(static events => events.Completion))).ConfigureAwait(false);
        lock (_gate)
        {
            if (_failure is null) _completion.TrySetResult();
            else _completion.TrySetException(_failure);
        }
    }

    private Task RequestStop()
    {
        lock (_gate) return _cancellation ??= _completion.Task.IsCompleted ? Task.CompletedTask : _stopping.CancelAsync();
    }

    private Task CancelControlsAsync()
    {
        lock (_gate) return _controlCancellation ??= _controlStopping.CancelAsync();
    }

    private void FaultControls(Exception error)
    {
        if (_produce is not null) _ = CancelControlsAsync();
        foreach (var input in _terminalInputs) input.Fault(error);
    }

    private bool RecordFailure(Exception error)
    {
        lock (_gate)
        {
            if (_completion.Task.IsCompleted || _failure is not null) return false;
            _failure = error;
        }
        FlowDiagnostics.LifecycleException(_component, Operation, error);
        return true;
    }

    private async Task ObserveCompletionAsync(Task completion)
    {
        try { await completion.ConfigureAwait(false); }
        catch (Exception error) { Fault(Unwrap(error)); }
    }

    private async Task ObserveOutputAsync(IDataflowBlock output)
    {
        try
        {
            await output.Completion.ConfigureAwait(false);
            if (Volatile.Read(ref _outputShutdownStarted) == 0)
                Fault(new InvalidOperationException(_produce is null
                    ? "A node data output completed before node processing stopped."
                    : "A source data output completed before source production stopped."));
        }
        catch (Exception error) { Fault(Unwrap(error)); }
    }

    private static Exception Unwrap(Exception error) => error is AggregateException aggregate ? aggregate.GetBaseException() : error;
}
