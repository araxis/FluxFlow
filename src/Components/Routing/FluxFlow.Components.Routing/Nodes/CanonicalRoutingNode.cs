using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Data;
using FluxFlow.Nodes;

namespace FluxFlow.Components.Routing.Nodes;

internal sealed class CanonicalRoutingNode<TOutput> : IFlowNode
{
    private readonly IFlowNode _operation;
    private readonly TransformBlock<FlowMessage, FlowMessage<JsonElement>> _input;
    private readonly IDisposable _inputLink;
    private readonly CanonicalRoutingOutput<TOutput> _output;
    private readonly Task _completion;
    private int _disposed;

    internal CanonicalRoutingNode(
        IFlowNode operation,
        ITargetBlock<FlowMessage<JsonElement>> input,
        ISourceBlock<FlowMessage<TOutput>> output,
        ISourceBlock<FlowMessage> events,
        int capacity)
    {
        _operation = operation;
        Events = events;
        _input = CanonicalRoutingInput.Create(capacity);
        _inputLink = _input.LinkTo(
            input,
            new DataflowLinkOptions { PropagateCompletion = true });
        _output = new CanonicalRoutingOutput<TOutput>(output, capacity);
        _completion = CompleteAsync();
    }

    public ITargetBlock<FlowMessage> Input => _input;

    public ISourceBlock<FlowMessage> Output => _output.Source;

    public ISourceBlock<FlowMessage> Events { get; }

    public Task Completion => _completion;

    public void Complete() => _input.Complete();

    public void Fault(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ((IDataflowBlock)_input).Fault(exception);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        Complete();
        try
        {
            await _completion.ConfigureAwait(false);
        }
        catch
        {
            // Completion remains the authoritative fault surface.
        }
        finally
        {
            _inputLink.Dispose();
            await _operation.DisposeAsync().ConfigureAwait(false);
            await _output.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task CompleteAsync()
    {
        await _operation.Completion.ConfigureAwait(false);
        await _output.Completion.ConfigureAwait(false);
    }
}

internal sealed class CanonicalJoinRoutingNode<TOutput> : IFlowNode
{
    private readonly IFlowNode _operation;
    private readonly TransformBlock<FlowMessage, FlowMessage<JsonElement>> _left;
    private readonly TransformBlock<FlowMessage, FlowMessage<JsonElement>> _right;
    private readonly IDisposable _leftLink;
    private readonly IDisposable _rightLink;
    private readonly CanonicalRoutingOutput<TOutput> _output;
    private readonly Task _completion;
    private int _disposed;

    internal CanonicalJoinRoutingNode(
        IFlowNode operation,
        ITargetBlock<FlowMessage<JsonElement>> left,
        ITargetBlock<FlowMessage<JsonElement>> right,
        ISourceBlock<FlowMessage<TOutput>> output,
        int capacity)
    {
        _operation = operation;
        _left = CanonicalRoutingInput.Create(capacity);
        _right = CanonicalRoutingInput.Create(capacity);
        _leftLink = _left.LinkTo(
            left,
            new DataflowLinkOptions { PropagateCompletion = true });
        _rightLink = _right.LinkTo(
            right,
            new DataflowLinkOptions { PropagateCompletion = true });
        _output = new CanonicalRoutingOutput<TOutput>(output, capacity);
        _completion = CompleteAsync();
    }

    public ITargetBlock<FlowMessage> Left => _left;

    public ITargetBlock<FlowMessage> Right => _right;

    public ISourceBlock<FlowMessage> Output => _output.Source;

    public Task Completion => _completion;

    public void Complete()
    {
        _left.Complete();
        _right.Complete();
    }

    public void Fault(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ((IDataflowBlock)_left).Fault(exception);
        ((IDataflowBlock)_right).Fault(exception);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        Complete();
        try
        {
            await _completion.ConfigureAwait(false);
        }
        catch
        {
            // Completion remains the authoritative fault surface.
        }
        finally
        {
            _leftLink.Dispose();
            _rightLink.Dispose();
            await _operation.DisposeAsync().ConfigureAwait(false);
            await _output.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task CompleteAsync()
    {
        await _operation.Completion.ConfigureAwait(false);
        await _output.Completion.ConfigureAwait(false);
    }
}

internal static class CanonicalRoutingInput
{
    internal static TransformBlock<FlowMessage, FlowMessage<JsonElement>> Create(int capacity)
        => new(
            Materialize,
            new ExecutionDataflowBlockOptions
            {
                BoundedCapacity = capacity,
                EnsureOrdered = true,
                MaxDegreeOfParallelism = 1
            });

    private static FlowMessage<JsonElement> Materialize(FlowMessage message)
    {
        if (message.IsError)
            return message.ConvertError<JsonElement>();

        var materialization = FlowValueMaterializers.JsonElement.Materialize(message.Value);
        return materialization.IsSuccess
            ? message.ConvertValue(materialization.Value)
            : message.WithError(materialization.Error!).ConvertError<JsonElement>();
    }
}

internal sealed class CanonicalRoutingOutput<TOutput> : IAsyncDisposable
{
    private readonly FlowOutput<FlowMessage> _output;
    private readonly ActionBlock<FlowMessage<TOutput>> _pump;
    private readonly IDisposable _link;
    private int _disposed;

    internal CanonicalRoutingOutput(
        ISourceBlock<FlowMessage<TOutput>> source,
        int capacity)
    {
        _output = new FlowOutput<FlowMessage>(
            new FlowOutputOptions { Capacity = capacity });
        _pump = new ActionBlock<FlowMessage<TOutput>>(
            CanonicalizeAsync,
            new ExecutionDataflowBlockOptions
            {
                BoundedCapacity = capacity,
                EnsureOrdered = true,
                MaxDegreeOfParallelism = 1
            });
        _link = source.LinkTo(
            _pump,
            new DataflowLinkOptions { PropagateCompletion = true });
        Completion = CompleteAsync();
    }

    internal ISourceBlock<FlowMessage> Source => _output;

    internal Task Completion { get; }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        _link.Dispose();
        await _output.DisposeAsync().ConfigureAwait(false);
    }

    private async Task CanonicalizeAsync(FlowMessage<TOutput> message)
    {
        var canonical = message.IsError
            ? message.ConvertError()
            : message.ConvertValue(FlowValue.From(message.Value));
        if (!await _output.SendAsync(canonical).ConfigureAwait(false))
            throw new InvalidOperationException("Routing output is unavailable.");
    }

    private async Task CompleteAsync()
    {
        try
        {
            await _pump.Completion.ConfigureAwait(false);
            _output.Complete();
            await _output.Completion.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _output.Fault(exception);
            throw;
        }
    }
}
