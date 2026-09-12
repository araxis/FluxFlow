using FluxFlow.Nodes;

namespace FluxFlow.Fluent;

/// <summary>Builds a canonical pipeline from directly constructed component instances.</summary>
public static class Flow
{
    public static FlowBuilder From(IFlowComponent source, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source is not IFlowSource)
            throw new ArgumentException("A pipeline must start with a source component.", nameof(source));

        var graph = new FlowGraphBuilder();
        return new FlowBuilder(graph, graph.Register(source, name).Output);
    }
}
