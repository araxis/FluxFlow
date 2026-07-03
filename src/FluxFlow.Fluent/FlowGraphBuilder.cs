using FluxFlow.Composition;

namespace FluxFlow.Fluent;

/// <summary>
/// Mutable accumulator shared by every <see cref="FlowBuilder{T}"/> in one fluent chain: it
/// collects the composed nodes, the links wiring them, and which nodes are entry (source) nodes,
/// then hands them to <see cref="CompositionRuntime.Create"/> when the chain is built. Kept
/// internal so the public surface is just the typed builder.
/// </summary>
internal sealed class FlowGraphBuilder
{
    private readonly List<ComposedNode> _nodes = new();
    private readonly List<IDisposable> _links = new();
    private readonly List<ComposedNode> _entryNodes = new();

    public void AddNode(ComposedNode node) => _nodes.Add(node);

    public void AddEntry(ComposedNode node)
    {
        _nodes.Add(node);
        _entryNodes.Add(node);
    }

    public void AddLink(IDisposable link) => _links.Add(link);

    public FlowGraph Build()
    {
        if (_entryNodes.Count == 0)
            throw new InvalidOperationException("A flow must start from at least one source (Flow.From).");

        return new FlowGraph(CompositionRuntime.Create(_nodes, _links, _entryNodes));
    }
}
