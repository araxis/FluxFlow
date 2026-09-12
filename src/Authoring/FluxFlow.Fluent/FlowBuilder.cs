using System.Threading.Tasks.Dataflow;
using FluxFlow.Core;
using FluxFlow.Nodes;

namespace FluxFlow.Fluent;

/// <summary>A readable chain over canonical component ports; materialization belongs to each component.</summary>
public sealed class FlowBuilder
{
    private readonly FlowGraphBuilder _graph;
    private readonly FlowOutputEndpoint _output;

    internal FlowBuilder(FlowGraphBuilder graph, FlowOutputEndpoint output)
    {
        _graph = graph;
        _output = output;
    }

    public FlowBuilder Then(IFlowComponent node, string? name = null)
    {
        var component = _graph.Register(node, name);
        _graph.Link(_output, component.Input);
        return new FlowBuilder(_graph, component.Output);
    }

    /// <summary>Feed a terminal side component without changing the main continuation; discard its unmatched outputs.</summary>
    public FlowBuilder Tap(IFlowComponent node, string? name = null)
    {
        var component = _graph.Register(node, name);
        _graph.Link(_output, component.Input);
        _graph.MarkTerminal(component);
        return this;
    }

    /// <summary>Continue from a declared output on a component already in this graph.</summary>
    public FlowBuilder Branch(ISourceBlock<FlowMessage> port, Action<FlowBuilder> build)
    {
        ArgumentNullException.ThrowIfNull(build);
        build(new FlowBuilder(_graph, _graph.GetOutput(port)));
        return this;
    }

    public FlowBuilder Apply(FlowSegment segment)
    {
        ArgumentNullException.ThrowIfNull(segment);
        return segment.ApplyTo(this);
    }

    /// <summary>End this chain at a component; discard its unmatched outputs, preserving any explicit links.</summary>
    public FlowTerminal To(IFlowComponent node, string? name = null)
    {
        var component = _graph.Register(node, name);
        _graph.Link(_output, component.Input);
        _graph.MarkTerminal(component);
        return new FlowTerminal(_graph);
    }

    /// <summary>Explicitly discard messages on this output only when no route matches.</summary>
    public FlowBuilder DiscardUnmatched()
    {
        _graph.DiscardUnmatched(_output);
        return this;
    }

    public FlowBuilder OnEvent(Action<FlowEvent> handler)
    {
        _graph.OnEvent(handler);
        return this;
    }

    public FlowGraph Build() => _graph.Build();

    internal bool SharesGraph(FlowBuilder other) => ReferenceEquals(_graph, other._graph);
}
