using FluxFlow.Nodes;

namespace FluxFlow.Fluent;

/// <summary>
/// The end of a flow's main line, after a sink was appended with
/// <see cref="FlowBuilder{T}.To{TIgnored}"/>. Call <see cref="Build"/> to produce the runnable
/// <see cref="FlowGraph"/>. Kept as its own type so a sink reads as terminal — you cannot keep
/// chaining processing onto it — while branches and error/event observers are still declared
/// before <see cref="Build"/>.
/// </summary>
public sealed class FlowTerminal
{
    private readonly FlowGraphBuilder _graph;

    internal FlowTerminal(FlowGraphBuilder graph) => _graph = graph;

    /// <summary>Observe every event the flow's nodes raise. Chainable; see <see cref="FlowGraph.OnEvent"/>.</summary>
    public FlowTerminal OnEvent(Action<FlowEvent> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        _graph.OnEvent(handler);
        return this;
    }

    /// <summary>Produce the runnable graph.</summary>
    public FlowGraph Build() => _graph.Build();
}
