using FluxFlow.Core.Messaging;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Core.Nodes;

/// <summary>
/// A zero-or-many-result processing component, bounded by default. Native transform ordering can preserve
/// input order across concurrent operations. Processing exceptions become error messages
/// without stopping other messages; explicit faults still stop the component.
/// </summary>
public abstract class FlowTransformNode : IFlowNode
{
    private readonly TransformManyBlock<FlowMessage, FlowMessage> _processor;
    private readonly FlowProcessingLifetime _lifetime;
    private readonly FlowOutput<FlowMessage> _output;
    private readonly BroadcastBlock<FlowMessage> _events = new(static message => message);
    private readonly ExecutionDataflowBlockOptions _options;
    private readonly int _outputCapacity;
    private readonly object _inputsGate = new();
    private bool _inputsExposed;

    protected FlowTransformNode(ExecutionDataflowBlockOptions? options = null, int outputCapacity = 128)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(outputCapacity);
        options ??= new ExecutionDataflowBlockOptions { BoundedCapacity = 128 };
        _options = options;
        _outputCapacity = outputCapacity;
        _output = new FlowOutput<FlowMessage>(new FlowOutputOptions { Capacity = outputCapacity });
        _processor = new TransformManyBlock<FlowMessage, FlowMessage>(message => RunAsync(message, TransformAsync), options);
        _lifetime = new FlowProcessingLifetime(_processor, _output, _events, this,
            flush: CompleteInputAsync, drain: ForwardAsync, cancellationToken: options.CancellationToken);
    }

    public ITargetBlock<FlowMessage> Input
    {
        get { lock (_inputsGate) { _inputsExposed = true; return _processor; } }
    }
    public ISourceBlock<FlowMessage> Output => _output;
    public ISourceBlock<FlowMessage> Events => _events;
    public IEnumerable<ITargetBlock<FlowMessage>> TerminalInputs => _lifetime.TerminalInputs.Cast<ITargetBlock<FlowMessage>>();

    public Task Completion => _lifetime.Completion;
    protected CancellationToken Stopping => _lifetime.Stopping;

    /// <summary>Returns zero or more messages. Results are enumerated within the error/tracing boundary.</summary>
    protected abstract Task<IEnumerable<FlowMessage>> TransformAsync(FlowMessage message);

    protected virtual bool HandlesErrors => false;
    private protected virtual bool EmitsDirectly => false;

    /// <summary>Sends immediately, with backpressure. Direct sends are not reordered by EnsureOrdered.</summary>
    protected ValueTask EmitAsync(FlowMessage message, CancellationToken cancellationToken = default)
        => _output.EmitAsync(message, cancellationToken, "The node data output is no longer accepting messages.");

    /// <summary>Creates an owned side output. Native processing order only orders returned main-output messages.</summary>
    protected FlowOutput<T> AddOutput<T>()
    {
        var output = new FlowOutput<T>(new FlowOutputOptions { Capacity = _outputCapacity });
        _lifetime.AddOutput(output);
        return output;
    }

    /// <summary>Sends to an owned side output with backpressure.</summary>
    protected static async ValueTask EmitAsync<T>(FlowOutput<T> output, T value,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        await output.EmitAsync(value, cancellationToken, "The node data output is no longer accepting messages.").ConfigureAwait(false);
    }

    /// <summary>Returns final main-output messages after input and processing drain. Not called on fault.</summary>
    protected virtual ValueTask<IEnumerable<FlowMessage>> FlushAsync()
        => ValueTask.FromResult<IEnumerable<FlowMessage>>([]);

    /// <summary>Creates an ordinary command input. Port naming belongs to composition.</summary>
    protected ITargetBlock<FlowMessage> AddInput(Func<FlowMessage, Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        lock (_inputsGate)
        {
            if (_inputsExposed || _processor.Completion.IsCompleted)
                throw new InvalidOperationException("Declare inputs during construction, before completing the node.");
            var input = new ActionBlock<FlowMessage>(async message =>
            {
                var results = await RunAsync(message, async item =>
                {
                    await action(item).ConfigureAwait(false);
                    return [];
                }).ConfigureAwait(false);
                foreach (var result in results)
                    await _output.EmitAsync(result, Stopping, "The node data output is no longer accepting messages.").ConfigureAwait(false);
            }, _options);
            _lifetime.AddInput(input);
            return input;
        }
    }

    /// <summary>Declares a bounded terminal control during construction. Name is a diagnostic label, not a registered port address.</summary>
    protected ITargetBlock<FlowMessage> AddControlInput(string name,
        Func<FlowMessage, CancellationToken, Task> action, int capacity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(action);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        lock (_inputsGate)
        {
            if (_inputsExposed || _processor.Completion.IsCompleted)
                throw new InvalidOperationException("Declare controls during construction, before completing the node.");
            var input = new ActionBlock<FlowMessage>(async message =>
            {
                using var activity = FlowTracing.StartProcessing(message, this, name);
                try { await action(message, Stopping).ConfigureAwait(false); }
                catch (Exception error)
                {
                    if (error is OperationCanceledException && Stopping.IsCancellationRequested) activity.Canceled();
                    else activity.Failed(error);
                    Fault(error);
                    throw;
                }
            }, new ExecutionDataflowBlockOptions { BoundedCapacity = capacity });
            _lifetime.AddTerminalInput(input);
            return input;
        }
    }

    /// <summary>Processes a payload and derives a result with the original message's lineage.</summary>
    protected async Task<IEnumerable<FlowMessage>> TransformAsync(FlowMessage message,
        Func<FlowValue, CancellationToken, ValueTask<FlowValue>> transform)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(transform);
        return [message.WithValue(await transform(message.Value, Stopping).ConfigureAwait(false))];
    }
    protected bool EmitEvent(FlowEvent @event) => _events.Post(@event);
    protected bool EmitEvent(FlowMessage @event) => _events.Post(@event);
    protected virtual ValueTask OnDisposeAsync() => ValueTask.CompletedTask;

    public void Complete() => _lifetime.Complete();
    public void Fault(Exception exception) => _lifetime.Fault(exception);
    public ValueTask DisposeAsync() => _lifetime.DisposeAsync(OnDisposeAsync);

    private async Task ForwardAsync(CancellationToken stopping)
    {
        // Receive one ordered result at a time; no intermediary block or queue.
        await foreach (var message in _processor.ReceiveAllAsync(stopping).ConfigureAwait(false))
            await _output.EmitAsync(message, stopping, "The node data output is no longer accepting messages.").ConfigureAwait(false);
    }

    private async ValueTask CompleteInputAsync()
    {
        var results = Materialize(await FlushAsync().ConfigureAwait(false));
        foreach (var message in results)
            await _output.EmitAsync(message, Stopping, "The node data output is no longer accepting messages.").ConfigureAwait(false);
    }

    private static FlowMessage[] Materialize(IEnumerable<FlowMessage> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        var materialized = results.ToArray();
        foreach (var result in materialized) ArgumentNullException.ThrowIfNull(result);
        return materialized;
    }

    private async Task<IEnumerable<FlowMessage>> RunAsync(FlowMessage message,
        Func<FlowMessage, Task<IEnumerable<FlowMessage>>> transform)
    {
        ArgumentNullException.ThrowIfNull(message);
        using var activity = FlowTracing.StartProcessing(message, this);
        var forwardError = message.IsError && !HandlesErrors;
        try
        {
            if (forwardError)
            {
                if (!EmitsDirectly) return [message];
                await EmitAsync(message, Stopping).ConfigureAwait(false);
                return [];
            }
            var results = await transform(message).ConfigureAwait(false);
            // Evaluate lazy results here: enumeration failures must become error data,
            // and derived messages must capture this operation's tracing context.
            return Materialize(results);
        }
        catch (Exception error)
        {
            if (error is OperationCanceledException && Stopping.IsCancellationRequested)
            {
                activity.Canceled();
                if (EmitsDirectly && !forwardError) return [];
                throw;
            }
            activity.Failed(error);
            FlowDiagnostics.ProcessingException(message, this, error);
            if (forwardError) throw;
            // Failed delivery is a lifetime fault, not a new processing error message.
            if (_output.Completion.IsFaulted) await _output.Completion.ConfigureAwait(false);
            var failure = message.WithValue(FlowProcessingError.FromException(error));
            if (!EmitsDirectly) return [failure];
            await EmitAsync(failure, Stopping).ConfigureAwait(false);
            return [];
        }
    }
}
