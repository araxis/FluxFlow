using FluxFlow.Core.Execution;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Messaging;

namespace FluxFlow.Core.Nodes;

/// <summary>
/// Base for a source without business inputs. Optional controls are terminal actions.
/// Normal data outputs are bounded and reliable;
/// events remain a best-effort observation stream.
/// </summary>
public abstract class FlowSource : IFlowSource
{
    private readonly FlowOutput<FlowMessage> _output;
    private readonly BroadcastBlock<FlowMessage> _events;
    private readonly FlowSourceLifetime _lifetime;
    private readonly object _portsGate = new();
    private bool _outputExposed;
    private readonly int _outputCapacity;

    protected FlowSource(int outputCapacity = 128)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(outputCapacity);
        _outputCapacity = outputCapacity;
        _output = new FlowOutput<FlowMessage>(new FlowOutputOptions { Capacity = outputCapacity });
        _events = new BroadcastBlock<FlowMessage>(static value => value);
        _lifetime = new FlowSourceLifetime(_output, _events, this, RunAsync);
    }

    public ISourceBlock<FlowMessage> Output
    {
        get { lock (_portsGate) { _outputExposed = true; return _output; } }
    }

    public ISourceBlock<FlowMessage> Events => _events;

    public IEnumerable<ITargetBlock<FlowMessage>> TerminalInputs => _lifetime.TerminalInputs.Cast<ITargetBlock<FlowMessage>>();

    public Task Completion => _lifetime.Completion;
    /// <summary>Production and accepted data outputs have drained; controls can remain open.</summary>
    public Task ProductionCompletion => _lifetime.ProductionCompletion;

    /// <summary>Declares a bounded terminal control action during construction. Name is a diagnostic label, not a registered port address.</summary>
    protected ITargetBlock<FlowMessage> AddControlInput(string name,
        Func<FlowMessage, CancellationToken, Task> action, int capacity)
    {
        lock (_portsGate)
        {
            if (_outputExposed || _lifetime.HasStarted)
                throw new InvalidOperationException("Declare controls during construction, before starting the source.");
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            ArgumentNullException.ThrowIfNull(action);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
            var input = new ActionBlock<FlowMessage>(async message =>
            {
                using var activity = FlowTracing.StartProcessing(message, this, name);
                try { await action(message, _lifetime.ControlStopping).ConfigureAwait(false); }
                catch (Exception error)
                {
                    if (error is OperationCanceledException && _lifetime.ControlStopping.IsCancellationRequested) activity.Canceled();
                    else activity.Failed(error);
                    Fault(error);
                    throw;
                }
            }, new ExecutionDataflowBlockOptions { BoundedCapacity = capacity });
            _lifetime.AddTerminalInput(input);
            return input;
        }
    }

    /// <summary>Signaled when stopping is requested or production has ended.</summary>
    protected CancellationToken Stopping => _lifetime.Stopping;

    public Task StartAsync(CancellationToken cancellationToken = default)
        => _lifetime.StartAsync(cancellationToken);

    /// <summary>
    /// Produce messages via <see cref="EmitAsync"/> until this returns or
    /// <paramref name="cancellationToken"/> is canceled.
    /// </summary>
    protected abstract Task RunAsync(CancellationToken cancellationToken);

    protected async ValueTask EmitAsync(
        FlowMessage message,
        CancellationToken cancellationToken = default)
        => await _output.EmitAsync(message, cancellationToken, "The source data output is no longer accepting messages.").ConfigureAwait(false);

    /// <summary>Reliably accepts a value on an additional output owned by this source.</summary>
    protected static async ValueTask EmitAsync<T>(
        FlowOutput<T> output,
        T value,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        await output.EmitAsync(value, cancellationToken, "The source data output is no longer accepting messages.").ConfigureAwait(false);
    }

    /// <summary>Creates an additional bounded reliable data output owned by this source.</summary>
    protected FlowOutput<T> AddOutput<T>()
    {
        var output = new FlowOutput<T>(new FlowOutputOptions { Capacity = _outputCapacity });
        _lifetime.AddOutput(output);
        return output;
    }

    protected ValueTask EmitErrorAsync(
        FlowError error,
        CancellationToken cancellationToken = default)
        => EmitAsync(FlowMessage.Create(error), cancellationToken);

    protected bool EmitEvent(FlowEvent @event) => _events.Post(@event);

    protected bool EmitEvent(FlowMessage @event) => _events.Post(@event);

    /// <summary>Stops production and closes control admission, draining accepted control actions.</summary>
    public void Complete() => _lifetime.Complete();

    /// <summary>Stops new data but keeps controls available for already accepted downstream work.</summary>
    public void StopProduction() => _lifetime.StopProduction();

    public void Fault(Exception exception) => _lifetime.Fault(exception);

    public ValueTask DisposeAsync() => _lifetime.DisposeAsync(OnDisposeAsync);

    protected virtual ValueTask OnDisposeAsync() => ValueTask.CompletedTask;
}
