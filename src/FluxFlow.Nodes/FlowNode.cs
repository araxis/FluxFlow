using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Data;

namespace FluxFlow.Nodes;

/// <summary>
/// Base for a single-input / single-output node. Inputs and normal data outputs are
/// bounded, so pressure from a slow downstream component reaches the producer instead
/// of silently replacing data. Events remain a best-effort observation stream.
/// </summary>
public abstract class FlowNode<TInput, TOutput> : IFlowNode
{
    private readonly ActionBlock<FlowMessage<TInput>> _processor;
    private readonly FlowOutput<FlowMessage<TOutput>> _output;
    private readonly BroadcastBlock<FlowEvent> _events;
    private readonly List<IDataflowBlock> _extraOutputs = [];
    private readonly CancellationTokenSource _stopping = new();
    private readonly TaskCompletionSource _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly int _outputCapacity;
    private int _outputShutdownStarted;
    private int _disposed;

    protected FlowNode(FlowNodeOptions? options = null)
    {
        options ??= new FlowNodeOptions();
        if (options.InputCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "InputCapacity must be greater than zero.");
        }

        if (options.OutputCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "OutputCapacity must be greater than zero.");
        }

        if (options.MaxDegreeOfParallelism <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "MaxDegreeOfParallelism must be greater than zero.");
        }

        _outputCapacity = options.OutputCapacity;
        _output = CreateOutput<FlowMessage<TOutput>>();
        _events = new BroadcastBlock<FlowEvent>(static value => value);
        _processor = new ActionBlock<FlowMessage<TInput>>(
            RunAsync,
            new ExecutionDataflowBlockOptions
            {
                BoundedCapacity = options.InputCapacity,
                MaxDegreeOfParallelism = options.MaxDegreeOfParallelism,
                EnsureOrdered = options.MaxDegreeOfParallelism == 1
            });

        _ = ObserveOutputTerminationAsync(_output);
        _ = CompleteWhenDrainedAsync();
    }

    /// <summary>Bounded input port. <c>SendAsync</c> applies backpressure.</summary>
    public ITargetBlock<FlowMessage<TInput>> Input => _processor;

    /// <summary>Bounded reliable normal-data output port.</summary>
    public ISourceBlock<FlowMessage<TOutput>> Output => _output;

    /// <summary>Best-effort event stream. Observers do not backpressure workflow data.</summary>
    public ISourceBlock<FlowEvent> Events => _events;

    /// <summary>Completes when input processing and every accepted data output have drained.</summary>
    public Task Completion => _completion.Task;

    /// <summary>Canceled when the node is faulted or disposed.</summary>
    protected CancellationToken Stopping => _stopping.Token;

    /// <summary>Handle one value message. Throwing is caught and emitted as error data.</summary>
    protected abstract Task ProcessAsync(FlowMessage<TInput> message);

    /// <summary>Override for components that deliberately inspect or recover error messages.</summary>
    protected virtual bool HandlesErrors => false;

    /// <summary>
    /// Reliably accepts a normal output message, waiting for bounded output capacity when
    /// downstream consumers are slow.
    /// </summary>
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

    /// <summary>
    /// Creates an additional bounded reliable data output owned by this node.
    /// </summary>
    protected FlowOutput<T> AddOutput<T>()
    {
        var output = CreateOutput<T>();
        _extraOutputs.Add(output);
        _ = ObserveOutputTerminationAsync(output);
        return output;
    }

    protected bool EmitEvent(FlowEvent @event) => _events.Post(@event);

    public void Complete() => _processor.Complete();

    public void Fault(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        Interlocked.Exchange(ref _outputShutdownStarted, 1);
        _stopping.Cancel();
        ((IDataflowBlock)_processor).Fault(exception);
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
        finally
        {
            try
            {
                await DisposeOutputsAsync().ConfigureAwait(false);
            }
            finally
            {
                _stopping.Cancel();
                _stopping.Dispose();
            }
        }
    }

    /// <summary>Override to release node-owned resources after the pump has stopped.</summary>
    protected virtual ValueTask OnDisposeAsync() => ValueTask.CompletedTask;

    /// <summary>
    /// Override to flush work deliberately retained by the node after input drains and
    /// before outputs complete. Await <see cref="EmitAsync"/> for normal data emitted here.
    /// This hook is not invoked on the fault path.
    /// </summary>
    protected virtual ValueTask OnInputCompletedAsync() => ValueTask.CompletedTask;

    private FlowOutput<T> CreateOutput<T>()
        => new(new FlowOutputOptions { Capacity = _outputCapacity });

    private async Task RunAsync(FlowMessage<TInput> message)
    {
        if (message.IsError && !HandlesErrors)
        {
            await EmitAsync(message.WithError<TOutput>(message.Error!), Stopping)
                .ConfigureAwait(false);
            return;
        }

        try
        {
            await ProcessAsync(message).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
            // Requested stop, not a processing failure.
        }
        catch (Exception exception)
        {
            if (_output.Completion.IsFaulted)
            {
                await _output.Completion.ConfigureAwait(false);
            }

            var details = JsonSerializer.SerializeToElement(new
            {
                exceptionType = exception.GetType().FullName
            });
            await EmitAsync(
                    message.WithError<TOutput>(new FlowError(
                        "node.processing_failed",
                        exception.Message,
                        "processing",
                        exception is TimeoutException,
                        details)),
                    Stopping)
                .ConfigureAwait(false);
        }
    }

    private async Task CompleteWhenDrainedAsync()
    {
        try
        {
            await _processor.Completion.ConfigureAwait(false);
            await OnInputCompletedAsync().ConfigureAwait(false);

            Interlocked.Exchange(ref _outputShutdownStarted, 1);
            CompleteOutputs();
            _events.Complete();

            var completions = new List<Task> { _output.Completion, _events.Completion };
            completions.AddRange(_extraOutputs.Select(static output => output.Completion));
            await Task.WhenAll(completions).ConfigureAwait(false);
            _completion.TrySetResult();
        }
        catch (Exception exception)
        {
            var unwrapped = Unwrap(exception);
            Interlocked.Exchange(ref _outputShutdownStarted, 1);
            _stopping.Cancel();
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
                !_processor.Completion.IsCompleted)
            {
                FaultProcessor(new InvalidOperationException(
                    "A node data output completed before node processing stopped."));
            }
        }
        catch (Exception exception)
        {
            FaultProcessor(Unwrap(exception));
        }
    }

    private void FaultProcessor(Exception exception)
    {
        if (_completion.Task.IsCompleted)
        {
            return;
        }

        _stopping.Cancel();
        try
        {
            ((IDataflowBlock)_processor).Fault(exception);
        }
        catch
        {
            // The processor may already be terminal; its completion remains authoritative.
        }
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

        throw new InvalidOperationException("The node data output is no longer accepting messages.");
    }

    private static Exception Unwrap(Exception exception)
        => exception is AggregateException aggregate
            ? aggregate.GetBaseException()
            : exception;
}
