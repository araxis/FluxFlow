using FluxFlow.Nodes;

namespace FluxFlow.Composition;

public sealed record ComponentNodeActivation<TNode>
    where TNode : IFlowNode
{
    public ComponentNodeActivation(
        TNode node,
        Task? completion = null,
        Func<ValueTask>? disposeAsync = null)
    {
        Node = node ?? throw new ArgumentNullException(nameof(node));
        Completion = completion;
        DisposeAsync = disposeAsync;
    }

    public TNode Node { get; }

    public Task? Completion { get; }

    public Func<ValueTask>? DisposeAsync { get; }
}
