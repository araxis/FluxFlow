using System.Threading.Tasks.Dataflow;
using FluxFlow.Nodes;

namespace FluxFlow.Composition;

public sealed class CompositionRuntime : IAsyncDisposable
{
    private readonly List<IDisposable> _links;
    private readonly List<IDisposable> _diagnosticLinks = [];
    private readonly HashSet<RuntimeNodeKey> _nodesWithIncomingLinks;
    private readonly BroadcastBlock<FlowEvent> _events = new(static value => value);
    private readonly BroadcastBlock<FlowError> _errors = new(static value => value);
    private int _disposed;

    internal CompositionRuntime(
        IReadOnlyList<CompositionRuntimeNode> nodes,
        IReadOnlyList<IDisposable> links,
        HashSet<RuntimeNodeKey> nodesWithIncomingLinks)
    {
        Nodes = nodes;
        _links = links.ToList();
        _nodesWithIncomingLinks = nodesWithIncomingLinks;
        foreach (var node in Nodes)
        {
            if (node.Descriptor.Events is not null)
                _diagnosticLinks.Add(node.Descriptor.Events.LinkTo(_events));

            if (node.Descriptor.Errors is not null)
                _diagnosticLinks.Add(node.Descriptor.Errors.LinkTo(_errors));
        }

        Completion = CompleteWhenNodesCompleteAsync();
    }

    /// <summary>
    /// Builds a runtime directly from already-composed node descriptors and the links wiring
    /// them together, without a <see cref="CompositionDefinition"/>, registry, or node names.
    /// Intended for code-first builders (for example the fluent DSL) that construct and link
    /// nodes themselves. <paramref name="entryNodes"/> are the source nodes with no incoming
    /// link: the runtime starts every <see cref="IFlowSource"/> and, on <see cref="StopAsync"/>,
    /// completes the entry nodes so completion propagates downstream. All three collections are
    /// captured by the runtime, which then owns the nodes' disposal.
    /// </summary>
    public static CompositionRuntime Create(
        IReadOnlyList<ComposedNode> nodes,
        IReadOnlyList<IDisposable> links,
        IReadOnlyList<ComposedNode> entryNodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(links);
        ArgumentNullException.ThrowIfNull(entryNodes);

        var entry = new HashSet<ComposedNode>(entryNodes);
        var runtimeNodes = new List<CompositionRuntimeNode>(nodes.Count);
        var nodesWithIncomingLinks = new HashSet<RuntimeNodeKey>();

        for (var index = 0; index < nodes.Count; index++)
        {
            var descriptor = nodes[index]
                ?? throw new ArgumentException("Composed nodes cannot be null.", nameof(nodes));

            // Names/definitions are synthetic here: a code-first graph has no registry type or
            // node name, and the runtime lifecycle only reads the descriptor + entry set.
            var key = new RuntimeNodeKey("flow", $"node-{index}");
            var definition = new NodeDefinition { Type = descriptor.Node.GetType().Name };
            runtimeNodes.Add(new CompositionRuntimeNode(key, definition, descriptor));

            if (!entry.Contains(descriptor))
                nodesWithIncomingLinks.Add(key);
        }

        return new CompositionRuntime(runtimeNodes, links, nodesWithIncomingLinks);
    }

    public IReadOnlyList<CompositionRuntimeNode> Nodes { get; }

    public ISourceBlock<FlowEvent> Events => _events;

    public ISourceBlock<FlowError> Errors => _errors;

    public Task Completion { get; }

    public async ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        foreach (var source in Nodes.Select(node => node.Node).OfType<IFlowSource>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            await source.StartAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        var entryNodes = Nodes
            .Where(node => !_nodesWithIncomingLinks.Contains(node.Key))
            .ToArray();

        if (entryNodes.Length == 0)
            entryNodes = Nodes.ToArray();

        foreach (var node in entryNodes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            node.Node.Complete();
        }

        await Completion.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        foreach (var node in Nodes.Reverse())
        {
            try
            {
                await node.Descriptor.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
                // Node completion/fault is exposed through Completion. Dispose must continue
                // so every owned node and link gets a teardown attempt.
            }
        }

        foreach (var link in _links)
            link.Dispose();

        foreach (var link in _diagnosticLinks)
            link.Dispose();

        try
        {
            await Completion.ConfigureAwait(false);
        }
        catch
        {
            // Completion remains the observable failure path.
        }
    }

    private async Task CompleteWhenNodesCompleteAsync()
    {
        try
        {
            await Task.WhenAll(Nodes.Select(node => node.Descriptor.Completion)).ConfigureAwait(false);
            _events.Complete();
            _errors.Complete();
            await Task.WhenAll(_events.Completion, _errors.Completion).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            ((IDataflowBlock)_events).Fault(exception);
            ((IDataflowBlock)_errors).Fault(exception);
            throw;
        }
    }
}
