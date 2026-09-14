using System.Threading.Tasks.Dataflow;
using FluxFlow.Data;

namespace FluxFlow.Nodes;

/// <summary>
/// Base for a source without business inputs. Optional controls are terminal actions.
/// Normal data outputs are bounded and reliable;
/// events remain a best-effort observation stream.
/// </summary>
public abstract class FlowSource : IFlowSource, IFlowComponent
{
    private readonly FlowOutput<FlowMessage> _output;
    private readonly BroadcastBlock<FlowMessage> _events;
    private readonly List<IDataflowBlock> _outputs = [];
    private readonly CancellationTokenSource _stopping = new();
    private readonly TaskCompletionSource _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _productionCompletion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _lifetimeGate = new();
    private readonly int _outputCapacity;
    private Task? _cancellation;
    private FlowControlInputs? _controls;
    private FlowPorts? _ports;
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
        _output = CreateOutput<FlowMessage>();
        _events = new BroadcastBlock<FlowMessage>(static value => value);
        _ = ObserveOutputTerminationAsync(_output);
    }

    public ISourceBlock<FlowMessage> Output => _output;

    public ISourceBlock<FlowMessage> Events => _events;

    public virtual FlowPorts Ports
    {
        get
        {
            lock (_lifetimeGate)
                return _ports ??= new FlowPorts(
                    outputs: new Dictionary<string, ISourceBlock<FlowMessage>> { [FlowPorts.OutputName] = Output },
                    events: Events, controls: _controls?.Inputs);
        }
    }

    public Task Completion => _completion.Task;
    /// <summary>Production and accepted data outputs have drained; controls can remain open.</summary>
    public Task ProductionCompletion => _productionCompletion.Task;

    /// <summary>Declares a bounded terminal control action during construction.</summary>
    protected ITargetBlock<FlowMessage> AddControlInput(string name,
        Func<FlowMessage, CancellationToken, Task> action, int capacity)
    {
        lock (_lifetimeGate)
        {
            if (_ports is not null || _started != 0 || _disposed != 0)
                throw new InvalidOperationException("Declare controls before exposing ports or starting the source.");
            return (_controls ??= new FlowControlInputs(this, Fault)).Add(name, action, capacity);
        }
    }

    /// <summary>Signaled when stopping is requested or production has ended.</summary>
    protected CancellationToken Stopping => _stopping.Token;

    public Task StartAsync(CancellationToken cancellationToken = default)
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

    /// <summary>
    /// Produce messages via <see cref="EmitAsync"/> until this returns or
    /// <paramref name="cancellationToken"/> is canceled.
    /// </summary>
    protected abstract Task RunAsync(CancellationToken cancellationToken);

    protected async ValueTask EmitAsync(
        FlowMessage message,
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
        _ = ObserveOutputTerminationAsync(output);
        return output;
    }

    protected ValueTask EmitErrorAsync(
        FlowError error,
        CancellationToken cancellationToken = default)
        => EmitAsync(FlowMessage.CreateError(error), cancellationToken);

    protected bool EmitEvent(FlowEvent @event) => _events.Post(@event);

    protected bool EmitEvent(FlowMessage @event) => _events.Post(@event);

    /// <summary>Stops production and closes control admission, draining accepted control actions.</summary>
    public void Complete()
    {
        _controls?.Complete();
        StopProduction();
    }

    /// <summary>Stops new data but keeps controls available for already accepted downstream work.</summary>
    public void StopProduction()
    {
        _ = RequestStop();
        if (Interlocked.CompareExchange(ref _started, 1, 0) == 0)
            _ = RunLifetimeAsync(produce: false);
    }

    public void Fault(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (!RecordFailure(exception)) return;
        Interlocked.Exchange(ref _outputShutdownStarted, 1);
        _ = RequestStop();
        _controls?.Fault(exception);
        FaultOutputs(exception);
        _events.Complete();
        if (Interlocked.CompareExchange(ref _started, 1, 0) == 0)
            _ = RunLifetimeAsync(produce: false);
    }

    public async ValueTask DisposeAsync()
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
            await OnDisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            FlowDiagnostics.LifecycleException(this, "source.dispose", exception);
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
                _controls?.Dispose();
                _stopping.Dispose();
            }
        }
    }

    protected virtual ValueTask OnDisposeAsync() => ValueTask.CompletedTask;

    private FlowOutput<T> CreateOutput<T>()
    {
        var output = new FlowOutput<T>(new FlowOutputOptions { Capacity = _outputCapacity });
        _outputs.Add(output);
        return output;
    }

    private async Task RunLifetimeAsync(bool produce)
    {
        try
        {
            if (produce)
            {
                _stopping.Token.ThrowIfCancellationRequested();
                await RunAsync(_stopping.Token).ConfigureAwait(false);
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
            FlowDiagnostics.LifecycleException(this, "source.stop", exception);
            lock (_lifetimeGate)
                _outputFailure = _outputFailure is null
                    ? Unwrap(exception)
                    : new AggregateException(_outputFailure, exception);
        }

        var failure = Volatile.Read(ref _outputFailure);
        if (failure is null) CompleteOutputs();
        else
        {
            _controls?.Fault(failure);
            FaultOutputs(failure);
        }
        try
        {
            await Task.WhenAll(_outputs.Select(static output => output.Completion)).ConfigureAwait(false);
        }
        catch (Exception exception) { RecordFailure(Unwrap(exception)); }

        if (Volatile.Read(ref _outputFailure) is { } productionFailure)
            _controls?.Fault(productionFailure);
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
            if (_controls is not null) await _controls.JoinAsync().ConfigureAwait(false);
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
        FlowDiagnostics.LifecycleException(this, "source", exception);
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
        foreach (var output in _outputs.Cast<IAsyncDisposable>())
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
