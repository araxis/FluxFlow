using System.Threading.Tasks.Dataflow;
using FluxFlow.Composition;
using FluxFlow.Nodes;

namespace FluxFlow.Fluent;

/// <summary>
/// A built, runnable flow. Wraps a <see cref="CompositionRuntime"/>: call <see cref="StartAsync"/>
/// to start the sources, await <see cref="Completion"/>, observe the aggregated
/// <see cref="Errors"/>/<see cref="Events"/> streams, and dispose to tear the graph down in order.
/// </summary>
public sealed class FlowGraph : IAsyncDisposable
{
    private readonly CompositionRuntime _runtime;

    internal FlowGraph(CompositionRuntime runtime) => _runtime = runtime;

    /// <summary>The underlying composition runtime, for advanced hosting scenarios.</summary>
    public CompositionRuntime Runtime => _runtime;

    /// <summary>Completes when every node has completed; faults if any node faults.</summary>
    public Task Completion => _runtime.Completion;

    /// <summary>Aggregated error stream across all nodes in the flow.</summary>
    public ISourceBlock<FlowError> Errors => _runtime.Errors;

    /// <summary>Aggregated event stream across all nodes in the flow.</summary>
    public ISourceBlock<FlowEvent> Events => _runtime.Events;

    /// <summary>Start every source node so the flow begins producing.</summary>
    public ValueTask StartAsync(CancellationToken cancellationToken = default)
        => _runtime.StartAsync(cancellationToken);

    /// <summary>Complete the entry nodes and await the flow draining to completion.</summary>
    public ValueTask StopAsync(CancellationToken cancellationToken = default)
        => _runtime.StopAsync(cancellationToken);

    /// <summary>Dispose every node and link in order.</summary>
    public ValueTask DisposeAsync() => _runtime.DisposeAsync();
}
