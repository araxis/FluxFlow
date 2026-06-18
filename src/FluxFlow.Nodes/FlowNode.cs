using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Nodes;

/// <summary>
/// Base for a single-input / single-output node. A node is a self-contained TPL
/// Dataflow processor: every message travels as a <see cref="FlowMessage{T}"/>
/// envelope (payload + correlation id). Post a <c>FlowMessage&lt;TInput&gt;</c> to
/// <see cref="Input"/> (a bounded buffer — backpressure on intake); the node
/// broadcasts a <c>FlowMessage&lt;TOutput&gt;</c> on <see cref="Output"/>, errors on
/// <see cref="Errors"/>, events on <see cref="Events"/>. Every source port is a
/// <see cref="BroadcastBlock{T}"/>, so one output can fan out to many downstream
/// nodes. No engine, registry, or runtime — just <c>new</c> the node and
/// <c>LinkTo</c> the next one. Transform a message with
/// <see cref="FlowMessage{T}.With{TOut}"/> to carry the correlation id forward.
/// </summary>
public abstract class FlowNode<TInput, TOutput> : IAsyncDisposable
{
    private readonly BufferBlock<FlowMessage<TInput>> _input;
    private readonly ActionBlock<FlowMessage<TInput>> _processor;
    private readonly BroadcastBlock<FlowMessage<TOutput>> _output;
    private readonly BroadcastBlock<FlowError> _errors;
    private readonly BroadcastBlock<FlowEvent> _events;
    private readonly CancellationTokenSource _stopping = new();
    private readonly TaskCompletionSource _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _disposed;

    protected FlowNode(FlowNodeOptions? options = null)
    {
        options ??= new FlowNodeOptions();
        if (options.InputCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options), "InputCapacity must be greater than zero.");
        }

        if (options.MaxDegreeOfParallelism <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options), "MaxDegreeOfParallelism must be greater than zero.");
        }

        _output = new BroadcastBlock<FlowMessage<TOutput>>(static message => message);
        _errors = new BroadcastBlock<FlowError>(static value => value);
        _events = new BroadcastBlock<FlowEvent>(static value => value);

        _input = new BufferBlock<FlowMessage<TInput>>(new DataflowBlockOptions
        {
            BoundedCapacity = options.InputCapacity
        });
        _processor = new ActionBlock<FlowMessage<TInput>>(
            RunAsync,
            new ExecutionDataflowBlockOptions
            {
                BoundedCapacity = options.MaxDegreeOfParallelism,
                MaxDegreeOfParallelism = options.MaxDegreeOfParallelism,
                EnsureOrdered = options.MaxDegreeOfParallelism == 1
            });
        _input.LinkTo(_processor, new DataflowLinkOptions { PropagateCompletion = true });
        _ = CompleteWhenDrainedAsync();
    }

    /// <summary>Input port — a bounded buffer; <c>SendAsync</c> applies backpressure.</summary>
    public ITargetBlock<FlowMessage<TInput>> Input => _input;

    /// <summary>Output port — broadcast; link it to as many downstream inputs as you like.</summary>
    public ISourceBlock<FlowMessage<TOutput>> Output => _output;

    /// <summary>Error port — broadcast; uniform <see cref="FlowError"/> stream.</summary>
    public ISourceBlock<FlowError> Errors => _errors;

    /// <summary>Event port — broadcast; uniform <see cref="FlowEvent"/> stream.</summary>
    public ISourceBlock<FlowEvent> Events => _events;

    /// <summary>Completes when the input has drained and all output ports are done.</summary>
    public Task Completion => _completion.Task;

    /// <summary>Canceled when the node is faulted or disposed.</summary>
    protected CancellationToken Stopping => _stopping.Token;

    /// <summary>Handle one message. Throwing is caught and surfaced on <see cref="Errors"/>.</summary>
    protected abstract Task ProcessAsync(FlowMessage<TInput> message);

    protected bool Emit(FlowMessage<TOutput> message) => _output.Post(message);

    protected bool EmitError(FlowError error) => _errors.Post(error);

    protected bool EmitEvent(FlowEvent @event) => _events.Post(@event);

    public void Complete() => _input.Complete();

    public void Fault(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        _stopping.Cancel();
        ((IDataflowBlock)_input).Fault(exception);
        ((IDataflowBlock)_processor).Fault(exception);
        ((IDataflowBlock)_output).Fault(exception);
        ((IDataflowBlock)_errors).Fault(exception);
        ((IDataflowBlock)_events).Fault(exception);
        _completion.TrySetException(exception);
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
            // Completion may surface a fault; teardown must still run.
        }

        try
        {
            await OnDisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            _stopping.Cancel();
            _stopping.Dispose();
        }
    }

    /// <summary>Override to release node-owned resources after the pump has stopped.</summary>
    protected virtual ValueTask OnDisposeAsync() => ValueTask.CompletedTask;

    private async Task RunAsync(FlowMessage<TInput> message)
    {
        try
        {
            await ProcessAsync(message).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
            // Requested stop, not a failure.
        }
        catch (Exception exception)
        {
            // Node-level safety net: a handler throw becomes an error item (stamped
            // with the in-flight correlation id), never a dead pump.
            EmitError(new FlowError
            {
                Timestamp = DateTimeOffset.UtcNow,
                CorrelationId = message.CorrelationId,
                Message = exception.Message,
                Exception = exception
            });
        }
    }

    private async Task CompleteWhenDrainedAsync()
    {
        try
        {
            await _processor.Completion.ConfigureAwait(false);
            _output.Complete();
            _errors.Complete();
            _events.Complete();
            await Task.WhenAll(_output.Completion, _errors.Completion, _events.Completion)
                .ConfigureAwait(false);
            _completion.TrySetResult();
        }
        catch (Exception exception)
        {
            ((IDataflowBlock)_output).Fault(exception);
            ((IDataflowBlock)_errors).Fault(exception);
            ((IDataflowBlock)_events).Fault(exception);
            _completion.TrySetException(exception);
        }
    }
}
