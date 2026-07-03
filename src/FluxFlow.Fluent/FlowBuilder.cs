using System.Threading.Tasks.Dataflow;
using FluxFlow.Composition;
using FluxFlow.Nodes;

namespace FluxFlow.Fluent;

/// <summary>
/// A type-safe builder for a linear (optionally fanned-out) flow. The generic parameter is the
/// payload type currently flowing out of the last node: <see cref="Then{TNext}"/> only accepts a
/// node whose input is <typeparamref name="T"/>, so wiring an incompatible node is a compile
/// error rather than a runtime failure. The <see cref="FlowMessage{T}"/> envelope is hidden — you
/// work in terms of payload types.
/// </summary>
public sealed class FlowBuilder<T>
{
    private static readonly DataflowLinkOptions PropagateCompletion = new() { PropagateCompletion = true };

    private readonly FlowGraphBuilder _graph;
    private readonly ISourceBlock<FlowMessage<T>> _output;

    internal FlowBuilder(FlowGraphBuilder graph, ISourceBlock<FlowMessage<T>> output)
    {
        _graph = graph;
        _output = output;
    }

    /// <summary>
    /// Append a processing node and continue the chain. The flow now carries
    /// <typeparamref name="TNext"/>. The current output is linked to <paramref name="node"/>'s
    /// input with completion propagation.
    /// </summary>
    public FlowBuilder<TNext> Then<TNext>(FlowNode<T, TNext> node)
    {
        ArgumentNullException.ThrowIfNull(node);

        _graph.AddNode(ComposedNode.Create(node, events: node.Events, errors: node.Errors));
        _graph.AddLink(_output.LinkTo(node.Input, PropagateCompletion));
        return new FlowBuilder<TNext>(_graph, node.Output);
    }

    /// <summary>
    /// Fan out: also feed the current payload to a side node (a logger, a metric, a second sink)
    /// without changing the main line, which stays on <typeparamref name="T"/>. The output port is
    /// a broadcast, so the same message reaches every tap and the continuation.
    /// </summary>
    public FlowBuilder<T> Tap<TIgnored>(FlowNode<T, TIgnored> node)
    {
        ArgumentNullException.ThrowIfNull(node);

        _graph.AddNode(ComposedNode.Create(node, events: node.Events, errors: node.Errors));
        _graph.AddLink(_output.LinkTo(node.Input, PropagateCompletion));
        return this;
    }

    /// <summary>
    /// Append a terminal (sink) node and build the runnable graph. The sink's own output, if any,
    /// is left unlinked.
    /// </summary>
    public FlowGraph To<TIgnored>(FlowNode<T, TIgnored> node)
    {
        Then(node);
        return _graph.Build();
    }

    /// <summary>Build the runnable graph, leaving the current output unlinked.</summary>
    public FlowGraph Build() => _graph.Build();
}
