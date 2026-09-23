using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Core.Nodes;

// Owns production and supplied blocks; never constructs ports or processes messages.
internal sealed class FlowSourceLifetime
{
    private readonly List<IDataflowBlock> _outputs = [];
    private readonly List<Func<ValueTask>> _releaseOutputs = [];
    private readonly CancellationTokenSource _stopping = new();
    private readonly TaskCompletionSource _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _productionCompletion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _lifetimeGate = new();
    private Task? _cancellation;
    private Exception? _outputFailure;
    private int _outputShutdownStarted;
    private int _started;
    private int _disposed;

    private readonly IDataflowBlock _events;
    private readonly object _component;
    private readonly Func<CancellationToken, Task> _produce;
    private readonly List<IDataflowBlock> _terminalInputs = [];
    private readonly CancellationTokenSource _aborting = new();
    private Task? _controlCancellation;

    internal FlowSourceLifetime(FlowOutput<Messaging.FlowMessage> output, IDataflowBlock events,
        object component, Func<CancellationToken, Task> produce)
    {
        _events = events;
        _component = component;
        _produce = produce;
        AddOutput(output);
    }

    internal Task Completion => _completion.Task;
    internal Task ProductionCompletion => _productionCompletion.Task;
    internal CancellationToken Stopping => _stopping.Token;
    internal CancellationToken ControlStopping => _aborting.Token;
    internal IEnumerable<IDataflowBlock> TerminalInputs => _terminalInputs;
    internal bool HasStarted => Volatile.Read(ref _started) != 0 || Volatile.Read(ref _disposed) != 0;

    internal void AddOutput<T>(FlowOutput<T> output)
    {
        _outputs.Add(output);
        _releaseOutputs.Add(output.DisposeAsync);
        _ = ObserveOutputTerminationAsync(output);
    }

    internal void AddTerminalInput(IDataflowBlock input)
    {
        _terminalInputs.Add(input);
        _ = ObserveTerminalInputAsync(input);
        if (_outputFailure is { } failure) input.Fault(failure);
    }

    private async Task ObserveTerminalInputAsync(IDataflowBlock input)
    {
        try { await input.Completion.ConfigureAwait(false); }
        catch (Exception error) { Fault(Unwrap(error)); }
    }

    private void FaultTerminalInputs(Exception error)
    {
        _ = CancelControlsAsync();
        foreach (var input in _terminalInputs) input.Fault(error);
    }

    private Task CancelControlsAsync()
    {
        lock (_lifetimeGate) return _controlCancellation ??= _aborting.CancelAsync();
    }

    internal Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled(cancellationToken);
        }

        if (Volatile.Read(ref _disposed) != 0 || Interlocked.CompareExchange(ref _started, 1, 0) != 0)
        {
            return Task.CompletedTask;
        }

        _ = RunLifetimeAsync(produce: true);
        return Task.CompletedTask;
    }

    /// <summary>Stops production and closes control admission, draining accepted control actions.</summary>
    internal void Complete()
    {
        foreach (var input in _terminalInputs) input.Complete();
        StopProduction();
    }

    /// <summary>Stops new data but keeps controls available for already accepted downstream work.</summary>
    internal void StopProduction()
    {
        _ = RequestStop();
        if (Interlocked.CompareExchange(ref _started, 1, 0) == 0)
            _ = RunLifetimeAsync(produce: false);
    }

    internal void Fault(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (!RecordFailure(exception)) return;
        Interlocked.Exchange(ref _outputShutdownStarted, 1);
        _ = RequestStop();
        FaultTerminalInputs(exception);
        FaultOutputs(exception);
        _events.Complete();
        if (Interlocked.CompareExchange(ref _started, 1, 0) == 0)
            _ = RunLifetimeAsync(produce: false);
    }

    internal async ValueTask DisposeAsync(Func<ValueTask> release)
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Complete();

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
            await release().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            FlowDiagnostics.LifecycleException(_component, "source.dispose", exception);
            throw;
        }
        finally
        {
            try
            {
                await DisposeOutputsAsync().ConfigureAwait(false);
            }
            finally
            {
                _aborting.Dispose();
                _stopping.Dispose();
            }
        }
    }

    private async Task RunLifetimeAsync(bool produce)
    {
        try
        {
            if (produce)
            {
                _stopping.Token.ThrowIfCancellationRequested();
                await _produce(_stopping.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
            // A requested stop is graceful unless a fault was recorded.
        }
        catch (Exception exception)
        {
            Fault(Unwrap(exception));
        }

        Interlocked.Exchange(ref _outputShutdownStarted, 1);
        try { await RequestStop().ConfigureAwait(false); }
        catch (Exception exception)
        {
            FlowDiagnostics.LifecycleException(_component, "source.stop", exception);
            lock (_lifetimeGate)
                _outputFailure = _outputFailure is null
                    ? Unwrap(exception)
                    : new AggregateException(_outputFailure, exception);
        }

        var failure = Volatile.Read(ref _outputFailure);
        if (failure is null) CompleteOutputs();
        else
        {
            FaultTerminalInputs(failure);
            FaultOutputs(failure);
        }
        try
        {
            await Task.WhenAll(_outputs.Select(static output => output.Completion)).ConfigureAwait(false);
        }
        catch (Exception exception) { RecordFailure(Unwrap(exception)); }

        if (Volatile.Read(ref _outputFailure) is { } productionFailure)
            FaultTerminalInputs(productionFailure);
        lock (_lifetimeGate)
        {
            if (_outputFailure is null) _productionCompletion.TrySetResult();
            else
            {
                _productionCompletion.TrySetException(_outputFailure);
                // The phase is optional to standalone callers; full Completion also reports this fault.
                _ = _productionCompletion.Task.Exception;
            }
        }
        try
        {
            try
            {
                await Task.WhenAll(_terminalInputs.Select(static input => input.Completion)).ConfigureAwait(false);
            }
            finally { await CancelControlsAsync().ConfigureAwait(false); }
        }
        catch (Exception exception) { RecordFailure(Unwrap(exception)); }
        _events.Complete();
        await _events.Completion.ConfigureAwait(false);

        lock (_lifetimeGate)
        {
            if (_outputFailure is null) _completion.TrySetResult();
            else _completion.TrySetException(_outputFailure);
        }
    }

    private Task RequestStop()
    {
        lock (_lifetimeGate)
            return _cancellation ??= _completion.Task.IsCompleted
                ? Task.CompletedTask
                : _stopping.CancelAsync();
    }

    private bool RecordFailure(Exception exception)
    {
        lock (_lifetimeGate)
        {
            if (_completion.Task.IsCompleted || _outputFailure is not null) return false;
            _outputFailure = exception;
        }
        FlowDiagnostics.LifecycleException(_component, "source", exception);
        return true;
    }

    private async Task ObserveOutputTerminationAsync(IDataflowBlock output)
    {
        try
        {
            await output.Completion.ConfigureAwait(false);
            if (Volatile.Read(ref _outputShutdownStarted) == 0 &&
                !_completion.Task.IsCompleted)
            {
                Fault(new InvalidOperationException(
                    "A source data output completed before source production stopped."));
            }
        }
        catch (Exception exception)
        {
            Fault(Unwrap(exception));
        }
    }

    private void CompleteOutputs()
    {
        foreach (var output in _outputs)
        {
            output.Complete();
        }
    }

    private void FaultOutputs(Exception exception)
    {
        foreach (var output in _outputs)
        {
            output.Fault(exception);
        }
    }

    private async ValueTask DisposeOutputsAsync()
    {
        foreach (var releaseOutput in _releaseOutputs)
        {
            await releaseOutput().ConfigureAwait(false);
        }
    }

    private static Exception Unwrap(Exception exception)
        => exception is AggregateException aggregate
            ? aggregate.GetBaseException()
            : exception;
}
