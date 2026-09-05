using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Routing.Contracts;
using FluxFlow.Components.Routing.Options;
using FluxFlow.Nodes;

namespace FluxFlow.Components.Routing.Nodes;

/// <summary>Joins two typed input streams into matched or timed-out outcomes.</summary>
public class JoinNode<TLeft, TRight> : IFlowNode
{
    private readonly JoinNodeRuntime<TLeft, TRight> _inner;

    public JoinNode(
        JoinRoutingOptions options,
        Func<TLeft, string?> leftKeySelector,
        Func<TRight, string?> rightKeySelector,
        string? engineName = null,
        TimeProvider? clock = null)
    {
        _inner = new JoinNodeRuntime<TLeft, TRight>(
            options,
            leftKeySelector,
            rightKeySelector,
            engineName,
            clock);
    }

    public ITargetBlock<FlowMessage<TLeft>> Left => _inner.Left;

    public ITargetBlock<FlowMessage<TRight>> Right => _inner.Right;

    public ISourceBlock<FlowMessage<FlowJoinOutcome<TLeft, TRight>>> Output => _inner.Output;

    public ISourceBlock<FlowMessage> Events => _inner.Events;

    public Task Completion => _inner.Completion;

    public void Complete() => _inner.Complete();

    public void Fault(Exception exception) => _inner.Fault(exception);

    public ValueTask DisposeAsync() => _inner.DisposeAsync();
}

/// <summary>Schema-less JSON join node for configuration-driven workflows.</summary>
public sealed class JsonJoinNode : IFlowNode
{
    private readonly JoinNode<JsonElement, JsonElement> _operation;
    private readonly CanonicalJoinRoutingNode<FlowJoinOutcome<JsonElement, JsonElement>> _inner;

    public JsonJoinNode(
        JoinRoutingOptions options,
        Func<JsonElement, string?> leftKeySelector,
        Func<JsonElement, string?> rightKeySelector,
        string? engineName = null,
        TimeProvider? clock = null)
    {
        _operation = new JoinNode<JsonElement, JsonElement>(
            options,
            leftKeySelector,
            rightKeySelector,
            engineName,
            clock);
        _inner = new CanonicalJoinRoutingNode<FlowJoinOutcome<JsonElement, JsonElement>>(
            _operation,
            _operation.Left,
            _operation.Right,
            _operation.Output,
            options.BoundedCapacity);
    }

    public ITargetBlock<FlowMessage> Left => _inner.Left;

    public ITargetBlock<FlowMessage> Right => _inner.Right;

    public ISourceBlock<FlowMessage> Output => _inner.Output;

    public ISourceBlock<FlowMessage> Events => _operation.Events;

    public Task Completion => _inner.Completion;

    public void Complete() => _inner.Complete();

    public void Fault(Exception exception) => _inner.Fault(exception);

    public ValueTask DisposeAsync() => _inner.DisposeAsync();
}
