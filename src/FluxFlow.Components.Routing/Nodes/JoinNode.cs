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

    public ISourceBlock<FlowEvent> Events => _inner.Events;

    public Task Completion => _inner.Completion;

    public void Complete() => _inner.Complete();

    public void Fault(Exception exception) => _inner.Fault(exception);

    public ValueTask DisposeAsync() => _inner.DisposeAsync();
}

/// <summary>Schema-less JSON join node for configuration-driven workflows.</summary>
public sealed class JsonJoinNode : JoinNode<JsonElement, JsonElement>
{
    public JsonJoinNode(
        JoinRoutingOptions options,
        Func<JsonElement, string?> leftKeySelector,
        Func<JsonElement, string?> rightKeySelector,
        string? engineName = null,
        TimeProvider? clock = null)
        : base(options, leftKeySelector, rightKeySelector, engineName, clock)
    {
    }
}
