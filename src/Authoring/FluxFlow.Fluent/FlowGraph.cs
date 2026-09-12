using System.Threading.Tasks.Dataflow;
using FluxFlow.Core;
using FluxFlow.Nodes;

namespace FluxFlow.Fluent;

/// <summary>A fluent view of the direct Core application, with best-effort native event observation.</summary>
public sealed class FlowGraph : IAsyncDisposable
{
    private readonly BroadcastBlock<FlowMessage> _events = new(static message => message);
    private readonly List<IDisposable> _links = [];
    private readonly object _gate = new();
    private Task? _dispose;

    internal FlowGraph(FlowApplication application, IReadOnlyList<ISourceBlock<FlowMessage>> eventSources)
    {
        Application = application;
        foreach (var source in eventSources)
            _links.Add(source.LinkTo(_events));
        _ = CompleteEventsAsync(eventSources);
    }

    public FlowApplication Application { get; }

    /// <summary>The Core completion task; available after StartAsync.</summary>
    public Task Completion => Application.Completion;

    public ISourceBlock<FlowMessage> Events => _events;

    /// <summary>Observes best-effort events through a bounded native target. Handler failures are isolated.</summary>
    public IDisposable OnEvent(Action<FlowEvent> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_dispose is not null, this);
            var sink = new ActionBlock<FlowMessage>(message =>
            {
                try
                {
                    if (message.TryGetFlowEvent(out var value)) handler(value);
                }
                catch
                {
                    // Diagnostic callbacks do not fault workflow processing.
                }
            }, new ExecutionDataflowBlockOptions { BoundedCapacity = 1 });
            var link = _events.LinkTo(sink, new DataflowLinkOptions { PropagateCompletion = true });
            _links.Add(link);
            return link;
        }
    }

    public ValueTask StartAsync(CancellationToken cancellationToken = default)
        => new(Application.StartAsync(cancellationToken));

    public ValueTask StopAsync(CancellationToken cancellationToken = default)
        => new(Application.StopAsync(cancellationToken));

    public ValueTask DisposeAsync()
    {
        lock (_gate) return new ValueTask(_dispose ??= DisposeCoreAsync());
    }

    private async Task DisposeCoreAsync()
    {
        try { await Application.DisposeAsync().ConfigureAwait(false); }
        finally
        {
            lock (_gate)
            {
                foreach (var link in _links) link.Dispose();
                _links.Clear();
                _events.Complete();
            }
        }
    }

    private async Task CompleteEventsAsync(IReadOnlyList<ISourceBlock<FlowMessage>> sources)
    {
        try
        {
            await Task.WhenAll(sources.Select(source => source.Completion)).ConfigureAwait(false);
            _events.Complete();
        }
        catch (Exception error) { ((IDataflowBlock)_events).Fault(error); }
    }
}
