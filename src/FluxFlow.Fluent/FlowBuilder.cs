using System.Threading.Tasks.Dataflow;
using FluxFlow.Composition;
using FluxFlow.Nodes;

namespace FluxFlow.Fluent;

/// <summary>
/// A type-safe builder for a flow. The generic parameter is the payload type currently flowing out
/// of the last node: <see cref="Then{TNext}"/> only accepts a node whose input is
/// <typeparamref name="T"/>, so wiring an incompatible node is a compile error rather than a
/// runtime failure. The <see cref="FlowMessage{T}"/> envelope is hidden — you work in payload types.
/// </summary>
public sealed class FlowBuilder<T>
{
    private readonly FlowGraphBuilder _graph;
    private readonly ISourceBlock<FlowMessage<T>> _output;

    internal FlowBuilder(FlowGraphBuilder graph, ISourceBlock<FlowMessage<T>> output)
    {
        _graph = graph;
        _output = output;
    }

    /// <summary>
    /// Append a processing node and continue the chain. The flow now carries
    /// <typeparamref name="TNext"/>.
    /// </summary>
    public FlowBuilder<TNext> Then<TNext>(FlowNode<T, TNext> node)
    {
        ArgumentNullException.ThrowIfNull(node);

        _graph.Register(ComposedNode.Create(node, events: node.Events, errors: node.Errors), isEntry: false);
        _graph.Link(_output, node, node.Input);
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

        _graph.Register(ComposedNode.Create(node, events: node.Events, errors: node.Errors), isEntry: false);
        _graph.Link(_output, node, node.Input);
        return this;
    }

    /// <summary>
    /// Start an independent sub-pipeline from a typed output port of a node already in this flow —
    /// for example a router's <c>Even</c>/<c>Odd</c> or a filter's <c>Passed</c>/<c>Failed</c> port.
    /// The sub-pipeline shares this flow's graph, so several branches can fan back into one node
    /// (pass the same node instance to <see cref="Then{TNext}"/>/<see cref="To{TIgnored}"/> in each
    /// branch). The main line is returned unchanged so branches chain fluently.
    /// </summary>
    public FlowBuilder<T> Branch<TBranch>(
        ISourceBlock<FlowMessage<TBranch>> port,
        Action<FlowBuilder<TBranch>> build)
    {
        ArgumentNullException.ThrowIfNull(port);
        ArgumentNullException.ThrowIfNull(build);

        build(new FlowBuilder<TBranch>(_graph, port));
        return this;
    }

    /// <summary>
    /// Append a terminal (sink) node. The sink's own output, if any, is left unlinked. Returns a
    /// <see cref="FlowTerminal"/> — call <see cref="FlowTerminal.Build"/> to produce the runnable
    /// graph. Passing the same sink instance from several branches fans them into one sink.
    /// </summary>
    public FlowTerminal To<TIgnored>(FlowNode<T, TIgnored> node)
    {
        ArgumentNullException.ThrowIfNull(node);

        _graph.Register(ComposedNode.Create(node, events: node.Events, errors: node.Errors), isEntry: false);
        _graph.Link(_output, node, node.Input);
        return new FlowTerminal(_graph);
    }

    /// <summary>Build the runnable graph, leaving the current output unlinked.</summary>
    public FlowGraph Build() => _graph.Build();
}
