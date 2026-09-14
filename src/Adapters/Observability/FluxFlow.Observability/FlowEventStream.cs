using System.Threading.Tasks.Dataflow;
using FluxFlow.Nodes;

namespace FluxFlow.Observability;

/// <summary>Caller-owned, best-effort observation of native component event sources.</summary>
public sealed class FlowEventStream : IAsyncDisposable
{
    private readonly BroadcastBlock<FlowMessage> _events = new(static message => message);
    private readonly List<IDisposable> _links = [];
    private readonly object _gate = new();
    private bool _disposed;
    private readonly CancellationTokenSource _stopping = new();
    private readonly Task _completion;

    public FlowEventStream(params ISourceBlock<FlowMessage>[] eventSources)
    {
        ArgumentNullException.ThrowIfNull(eventSources);
        foreach (var source in eventSources) ArgumentNullException.ThrowIfNull(source);
        foreach (var source in eventSources)
            _links.Add(source.LinkTo(_events));
        _completion = CompleteEventsAsync(eventSources);
    }

    public ISourceBlock<FlowMessage> Events => _events;

    /// <summary>Observes best-effort events through a bounded native target. Handler failures are isolated.</summary>
    public IDisposable OnEvent(Action<FlowEvent> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
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

    public async ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var link in _links) link.Dispose();
            _links.Clear();
            _events.Complete();
            _stopping.Cancel();
        }
        await _completion.ConfigureAwait(false);
        _stopping.Dispose();
    }
    private async Task CompleteEventsAsync(IReadOnlyList<ISourceBlock<FlowMessage>> sources)
    {
        try
        {
            await Task.WhenAll(sources.Select(source => source.Completion)).WaitAsync(_stopping.Token).ConfigureAwait(false);
            _events.Complete();
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested) { }
        catch (Exception error) { ((IDataflowBlock)_events).Fault(error); }
    }
}
