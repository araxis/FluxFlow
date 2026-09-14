using System.Runtime.ExceptionServices;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Nodes;

/// <summary>
/// Base for a single-input / single-output node. Inputs and normal data outputs are
/// bounded, so pressure from a slow downstream component reaches the producer instead
/// of silently replacing data. Events remain a best-effort observation stream.
/// </summary>
public abstract class FlowNode : IFlowComponent
{
    private readonly ActionBlock<FlowMessage> _processor;
    private readonly FlowProcessingLifetime _lifetime;
    private Task? _completion;
    private readonly object _controlGate = new();
    private FlowControlInputs? _controls;
    private FlowPorts? _ports;

    protected FlowNode(FlowNodeOptions? options = null)
    {
        options ??= new FlowNodeOptions();
        _lifetime = new FlowProcessingLifetime(options.OutputCapacity, this);
        _processor = new ActionBlock<FlowMessage>(
            RunAsync,
            new ExecutionDataflowBlockOptions
            {
                BoundedCapacity = options.InputCapacity,
                MaxDegreeOfParallelism = options.MaxDegreeOfParallelism,
                EnsureOrdered = options.MaxDegreeOfParallelism == 1
            });

        _lifetime.Initialize(OnInputCompletedAsync, _processor);
    }

    /// <summary>Bounded input port. <c>SendAsync</c> applies backpressure.</summary>
    public ITargetBlock<FlowMessage> Input => _processor;

    /// <summary>Bounded reliable normal-data output port.</summary>
    public ISourceBlock<FlowMessage> Output => _lifetime.Output;

    /// <summary>Best-effort event stream. Observers do not backpressure workflow data.</summary>
    public ISourceBlock<FlowMessage> Events => _lifetime.Events;

    public virtual FlowPorts Ports
    {
        get
        {
            lock (_controlGate)
                return _ports ??= new FlowPorts(
                    inputs: new Dictionary<string, ITargetBlock<FlowMessage>> { [FlowPorts.InputName] = Input },
                    outputs: new Dictionary<string, ISourceBlock<FlowMessage>> { [FlowPorts.OutputName] = Output },
                    events: Events, controls: _controls?.Inputs);
        }
    }

    /// <summary>Declares a bounded terminal control during construction, before exposing Ports.</summary>
    protected ITargetBlock<FlowMessage> AddControlInput(string name,
        Func<FlowMessage, CancellationToken, Task> action, int capacity)
    {
        lock (_controlGate)
        {
            if (_ports is not null || _processor.Completion.IsCompleted)
                throw new InvalidOperationException("Declare controls before exposing ports or completing the node.");
            if (name == FlowPorts.InputName)
                throw new ArgumentException("Control port conflicts with the business input.", nameof(name));
            var input = (_controls ??= new FlowControlInputs(this, Fault)).Add(name, action, capacity);
            _completion ??= CompleteControlsAsync();
            return input;
        }
    }

    /// <summary>Completes when input processing, accepted data outputs and terminal controls have drained.</summary>
    public Task Completion => _completion ?? _lifetime.Completion;

    /// <summary>Canceled when the node is faulted or disposed.</summary>
    protected CancellationToken Stopping => _lifetime.Stopping;

    /// <summary>Handle one value message. Throwing is caught and emitted as error data.</summary>
    protected abstract Task ProcessAsync(FlowMessage message);

    /// <summary>Override for components that deliberately inspect or recover error messages.</summary>
    protected virtual bool HandlesErrors => false;

    /// <summary>
    /// Reliably accepts a normal output message, waiting for bounded output capacity when
    /// downstream consumers are slow.
    /// </summary>
    protected ValueTask EmitAsync(
        FlowMessage message,
        CancellationToken cancellationToken = default)
        => FlowProcessingLifetime.EmitAsync(_lifetime.Output, message, cancellationToken);

    /// <summary>
    /// Reliably accepts a value on an additional output created by <see cref="AddOutput{T}"/>.
    /// </summary>
    protected static ValueTask EmitAsync<T>(
        FlowOutput<T> output,
        T value,
        CancellationToken cancellationToken = default)
        => FlowProcessingLifetime.EmitAsync(output, value, cancellationToken);

    /// <summary>
    /// Creates an additional bounded reliable data output owned by this node.
    /// </summary>
    protected FlowOutput<T> AddOutput<T>() => _lifetime.AddOutput<T>();

    protected bool EmitEvent(FlowEvent @event) => _lifetime.Events.Post(@event);

    protected bool EmitEvent(FlowMessage @event) => _lifetime.Events.Post(@event);

    public void Complete() => _lifetime.Complete();

    public void Fault(Exception exception) => _lifetime.Fault(exception);

    public ValueTask DisposeAsync() => _lifetime.DisposeAsync(async () =>
    {
        try { await Completion.ConfigureAwait(false); }
        catch { /* Completion retains the processing/control fault. */ }
        try { await OnDisposeAsync().ConfigureAwait(false); }
        finally { _controls?.Dispose(); }
    });

    private async Task CompleteControlsAsync()
    {
        Exception? failure = null;
        try { await _lifetime.Completion.ConfigureAwait(false); }
        catch (Exception error) { failure = error; }
        // Keep controls alive while accepted business outputs drain, including feedback.
        if (_controls is not null)
        {
            if (failure is null) _controls.Complete();
            else _controls.Fault(failure);
            try { await _controls.JoinAsync().ConfigureAwait(false); }
            catch (Exception error) { failure ??= error; }
        }
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    /// <summary>Override to release node-owned resources after the pump has stopped.</summary>
    protected virtual ValueTask OnDisposeAsync() => ValueTask.CompletedTask;

    /// <summary>
    /// Override to flush work deliberately retained by the node after input drains and
    /// before outputs complete. Await <see cref="EmitAsync"/> for normal data emitted here.
    /// This hook is not invoked on the fault path.
    /// </summary>
    protected virtual ValueTask OnInputCompletedAsync() => ValueTask.CompletedTask;

    private async Task RunAsync(FlowMessage message)
    {
        using var activity = FlowTracing.StartProcessing(message, this);
        var forwardError = message.IsError && !HandlesErrors;
        try
        {
            if (forwardError) await EmitAsync(message, Stopping).ConfigureAwait(false);
            else await ProcessAsync(message).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (Stopping.IsCancellationRequested)
        {
            activity.Canceled();
            if (forwardError) throw;
            // Requested stop, not a processing failure.
        }
        catch (Exception exception)
        {
            activity.Failed(exception);
            FlowDiagnostics.ProcessingException(message, this, exception);
            // Forwarding failures remain terminal; do not replace an existing error envelope.
            if (forwardError) throw;
            if (Output.Completion.IsFaulted)
            {
                await Output.Completion.ConfigureAwait(false);
            }

            await EmitAsync(
                    message.WithError(FlowProcessingError.FromException(exception)),
                    Stopping)
                .ConfigureAwait(false);
        }
    }
}
