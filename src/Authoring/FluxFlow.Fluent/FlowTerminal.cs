using FluxFlow.Nodes;

namespace FluxFlow.Fluent;

/// <summary>The end of the main chain; branches may share the same terminal component.</summary>
public sealed class FlowTerminal
{
    private readonly FlowGraphBuilder _graph;

    internal FlowTerminal(FlowGraphBuilder graph) => _graph = graph;

    public FlowTerminal OnEvent(Action<FlowEvent> handler)
    {
        _graph.OnEvent(handler);
        return this;
    }

    public FlowGraph Build() => _graph.Build();
}
