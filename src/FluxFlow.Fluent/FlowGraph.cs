using System.Threading.Tasks.Dataflow;
using FluxFlow.Composition;
using FluxFlow.Nodes;

namespace FluxFlow.Fluent;

/// <summary>
/// A built, runnable flow. Wraps a <see cref="CompositionRuntime"/>: call <see cref="StartAsync"/>
/// to start the sources, await <see cref="Completion"/>, observe the aggregated
/// <see cref="Events"/> stream, and dispose to tear the graph down in order.
/// </summary>
public sealed class FlowGraph : IAsyncDisposable
{
    private readonly CompositionRuntime _runtime;
    private readonly List<IDisposable> _subscriptions = new();

    internal FlowGraph(CompositionRuntime runtime) => _runtime = runtime;

    /// <summary>The underlying composition runtime, for advanced hosting scenarios.</summary>
    public CompositionRuntime Runtime => _runtime;

    /// <summary>Completes when every node has completed; faults if any node faults.</summary>
    public Task Completion => _runtime.Completion;

    /// <summary>Aggregated event stream across all nodes in the flow.</summary>
    public ISourceBlock<FlowEvent> Events => _runtime.Events;

    /// <summary>
    /// Observe every event the flow's nodes raise. A throwing handler is isolated so it cannot
    /// break observation.
    /// </summary>
    public IDisposable OnEvent(Action<FlowEvent> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return Subscribe(_runtime.Events, handler);
    }

    /// <summary>Start every source node so the flow begins producing.</summary>
    public ValueTask StartAsync(CancellationToken cancellationToken = default)
        => _runtime.StartAsync(cancellationToken);

    /// <summary>Complete the entry nodes and await the flow draining to completion.</summary>
    public ValueTask StopAsync(CancellationToken cancellationToken = default)
        => _runtime.StopAsync(cancellationToken);

    /// <summary>Dispose every node and link in order, and tear down error/event subscriptions.</summary>
    public async ValueTask DisposeAsync()
    {
        // Dispose the runtime first: it completes the nodes and the aggregated event stream,
        // which completes the subscription sinks (they are linked with completion propagation).
        await _runtime.DisposeAsync().ConfigureAwait(false);

        foreach (var subscription in _subscriptions)
            subscription.Dispose();
    }

    private IDisposable Subscribe<T>(ISourceBlock<T> source, Action<T> handler)
    {
        var sink = new ActionBlock<T>(item =>
        {
            // An observer must not be able to break the subscription or fault the shared stream,
            // so a handler bug is isolated here rather than tearing observation down.
            try
            {
                handler(item);
            }
            catch
            {
                // Intentionally swallowed: OnError/OnEvent are diagnostic sinks.
            }
        });

        var link = source.LinkTo(sink, new DataflowLinkOptions { PropagateCompletion = true });
        _subscriptions.Add(link);
        return link;
    }
}
