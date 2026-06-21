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
