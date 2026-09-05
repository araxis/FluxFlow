using System.Threading.Tasks.Dataflow;
using FluxFlow.Composition.Model;
using FluxFlow.Nodes;

namespace FluxFlow.Composition;

public sealed class ApplicationRuntime : IAsyncDisposable
{
    private readonly List<IDisposable> _links;
    private readonly List<IDisposable> _diagnosticLinks = [];
    private readonly List<ActionBlock<FlowMessage>> _eventTargets = [];
    private readonly HashSet<RuntimeNodeKey> _nodesWithIncomingLinks;
    private readonly BroadcastBlock<FlowMessage> _activity = new(static value => value);
    private int _disposed;

    internal ApplicationRuntime(
        IReadOnlyList<ApplicationRuntimeComponent> nodes,
        IReadOnlyList<IDisposable> links,
        HashSet<RuntimeNodeKey> nodesWithIncomingLinks)
    {
        Nodes = nodes;
        _links = links.ToList();
        _nodesWithIncomingLinks = nodesWithIncomingLinks;
        foreach (var node in Nodes)
        {
            if (node.Descriptor.Events is not null)
            {
                var target = new ActionBlock<FlowMessage>(message =>
                {
                    node.Descriptor.NotifyEvent(message);
                    _activity.Post(message);
                }, new ExecutionDataflowBlockOptions { BoundedCapacity = 256 });
                _eventTargets.Add(target);
                _diagnosticLinks.Add(node.Descriptor.Events.LinkTo(target,
                    new DataflowLinkOptions { PropagateCompletion = false }));
            }
        }

        Completion = CompleteWhenNodesCompleteAsync();
    }

    /// <summary>
    /// Builds a runtime directly from already-composed node descriptors and the links wiring
    /// them together, without a persisted application definition or component names.
    /// Intended for code-first builders (for example the fluent DSL) that construct and link
    /// nodes themselves. <paramref name="entryNodes"/> are the source nodes with no incoming
    /// link: the runtime starts every <see cref="IFlowSource"/> and, on <see cref="StopAsync"/>,
    /// completes the entry nodes so completion propagates downstream. All three collections are
    /// captured by the runtime, which then owns the nodes' disposal.
    /// </summary>
    public static ApplicationRuntime Create(
        IReadOnlyList<ComponentInstance> nodes,
        IReadOnlyList<IDisposable> links,
        IReadOnlyList<ComponentInstance> entryNodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(links);
        ArgumentNullException.ThrowIfNull(entryNodes);

        var entry = new HashSet<ComponentInstance>(entryNodes);
        var runtimeNodes = new List<ApplicationRuntimeComponent>(nodes.Count);
        var nodesWithIncomingLinks = new HashSet<RuntimeNodeKey>();

        for (var index = 0; index < nodes.Count; index++)
        {
            var descriptor = nodes[index]
                ?? throw new ArgumentException("Composed nodes cannot be null.", nameof(nodes));

            // Code-first graphs do not have persisted component declarations. Keep a minimal
            // canonical descriptor so runtime inspection still uses component terminology.
            var key = new RuntimeNodeKey("flow", $"node-{index}");
            var component = new ComponentDefinition(descriptor.Node.GetType().Name);
            runtimeNodes.Add(new ApplicationRuntimeComponent(key, component, descriptor));

            if (!entry.Contains(descriptor))
                nodesWithIncomingLinks.Add(key);
        }

        return new ApplicationRuntime(runtimeNodes, links, nodesWithIncomingLinks);
    }

    public IReadOnlyList<ApplicationRuntimeComponent> Nodes { get; }

    public ISourceBlock<FlowMessage> Activity => _activity;

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

        var cleanupExceptions = new List<Exception>();

        foreach (var node in Nodes.Reverse())
        {
            try
            {
                await node.Descriptor.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                cleanupExceptions.Add(exception);
            }
        }

        foreach (var link in _links)
        {
            try
            {
                link.Dispose();
            }
            catch (Exception exception)
            {
                cleanupExceptions.Add(exception);
            }
        }

        foreach (var link in _diagnosticLinks)
        {
            try
            {
                link.Dispose();
            }
            catch (Exception exception)
            {
                cleanupExceptions.Add(exception);
            }
        }

        try
        {
            await Completion.ConfigureAwait(false);
        }
        catch
        {
            // Completion remains the observable failure path.
        }

        if (cleanupExceptions.Count > 0)
        {
            throw new AggregateException(
                "One or more composition runtime resources failed during disposal.",
                cleanupExceptions);
        }
    }

    private async Task CompleteWhenNodesCompleteAsync()
    {
        try
        {
            await Task.WhenAll(Nodes.Select(node => node.Descriptor.Completion)).ConfigureAwait(false);
            foreach (var target in _eventTargets) target.Complete();
            await Task.WhenAll(_eventTargets.Select(target => target.Completion)).ConfigureAwait(false);
            _activity.Complete();
            await _activity.Completion.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            foreach (var target in _eventTargets) target.Complete();
            ((IDataflowBlock)_activity).Fault(exception);
            throw;
        }
    }
}
