using System.Threading.Tasks.Dataflow;
using FluxFlow.Composition;
using FluxFlow.Nodes;

namespace FluxFlow.Fluent;

/// <summary>
/// Mutable accumulator shared by every <see cref="FlowBuilder{T}"/> in one fluent chain: it
/// collects the composed nodes (de-duplicated by reference so the same node can be a fan-in
/// target of several branches), the links wiring them, and which nodes are entry (source) nodes.
/// <see cref="Build"/> wires completion and hands everything to
/// <see cref="CompositionRuntime.Create"/>.
/// </summary>
/// <remarks>
/// Links are created without TPL Dataflow's <c>PropagateCompletion</c>. Instead, each node is
/// completed once <em>all</em> of its upstream sources finish (<see cref="Build"/>). Propagating
/// completion per-link would let the first upstream of a fan-in node complete it prematurely and
/// drop the other branches' messages; waiting for every upstream is correct for linear, fan-out,
/// and fan-in graphs alike, and faults propagate the same way.
/// </remarks>
internal sealed class FlowGraphBuilder
{
    private readonly List<ComposedNode> _nodes = new();
    private readonly HashSet<IFlowNode> _registered = new(ReferenceEqualityComparer.Instance);
    private readonly List<ComposedNode> _entryNodes = new();
    private readonly List<IDisposable> _links = new();
    private readonly Dictionary<IFlowNode, List<Task>> _upstreamCompletions =
        new(ReferenceEqualityComparer.Instance);

    /// <summary>Register a node once (by reference). Repeated registrations of the same node — as
    /// happens when several branches fan into one sink — are ignored.</summary>
    public void Register(ComposedNode node, bool isEntry)
    {
        if (!_registered.Add(node.Node))
            return;

        _nodes.Add(node);
        if (isEntry)
            _entryNodes.Add(node);
    }

    /// <summary>Link a typed source port to a target node's input and record the completion edge.</summary>
    public void Link<T>(
        ISourceBlock<FlowMessage<T>> source,
        IFlowNode targetNode,
        ITargetBlock<FlowMessage<T>> targetInput)
    {
        _links.Add(source.LinkTo(targetInput));

        if (!_upstreamCompletions.TryGetValue(targetNode, out var completions))
        {
            completions = new List<Task>();
            _upstreamCompletions[targetNode] = completions;
        }

        completions.Add(source.Completion);
    }

    public FlowGraph Build()
    {
        if (_entryNodes.Count == 0)
            throw new InvalidOperationException("A flow must start from at least one source (Flow.From).");

        foreach (var (target, completions) in _upstreamCompletions)
            _ = CompleteWhenUpstreamsFinishAsync(completions, target);

        return new FlowGraph(CompositionRuntime.Create(_nodes, _links, _entryNodes));
    }

    private static async Task CompleteWhenUpstreamsFinishAsync(List<Task> upstreams, IFlowNode target)
    {
        try
        {
            await Task.WhenAll(upstreams).ConfigureAwait(false);
            target.Complete();
        }
        catch (Exception exception)
        {
            target.Fault(exception);
        }
    }
}
