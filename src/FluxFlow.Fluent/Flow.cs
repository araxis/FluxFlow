using FluxFlow.Composition;
using FluxFlow.Nodes;

namespace FluxFlow.Fluent;

/// <summary>
/// Entry point for the FluxFlow fluent DSL: compose standalone nodes into a runnable graph with
/// wiring the compiler checks for you. Start with a source, chain nodes with
/// <see cref="FlowBuilder{T}.Then{TNext}"/>, and <c>Build</c> — the <see cref="FlowMessage{T}"/>
/// envelope, links, lifecycle, and disposal are handled.
/// </summary>
/// <example>
/// <code>
/// await using var flow = Flow
///     .From(new TickSource())              // FlowSource&lt;DateTimeOffset&gt;
///     .Then(new FormatNode())              // FlowNode&lt;DateTimeOffset, string&gt;
///     .To(new ConsoleSink());              // FlowNode&lt;string, string&gt;
/// await flow.StartAsync();
/// await flow.Completion;
/// </code>
/// </example>
public static class Flow
{
    /// <summary>Start a flow from a source node. The flow now carries <typeparamref name="T"/>.</summary>
    public static FlowBuilder<T> From<T>(FlowSource<T> source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var graph = new FlowGraphBuilder();
        graph.Register(ComposedNode.Create(source, events: source.Events), isEntry: true);
        return new FlowBuilder<T>(graph, source.Output);
    }
}
