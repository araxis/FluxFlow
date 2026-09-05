using System.Threading.Tasks.Dataflow;
using FluxFlow.Nodes;

namespace FluxFlow.Engine.Events;

/// <summary>Application-lifetime fan-in of canonical events; no history, queries or report definitions.</summary>
internal sealed class ApplicationEventStream : IAsyncDisposable
{
    private readonly BroadcastBlock<FlowMessage> _events;
    private readonly string? _application;

    internal ApplicationEventStream(ApplicationEventOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.Capacity is < 1 or > 65536)
            throw new ArgumentOutOfRangeException(nameof(options), "Event capacity must be between one and 65536.");
        _application = options.Application;
        _events = new BroadcastBlock<FlowMessage>(static message => message,
            new DataflowBlockOptions { BoundedCapacity = options.Capacity });
    }

    internal ISourceBlock<FlowMessage> Events => _events;

    internal void Publish(FlowMessage message, string? revisionId = null)
    {
        ArgumentNullException.ThrowIfNull(message);
        var headers = message.Headers.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        if (_application is not null) headers[FlowEventHeaders.Application] = _application;
        if (revisionId is not null) headers[FlowEventHeaders.Revision] = revisionId;
        var observation = message.IsError
            ? FlowMessage.RestoreError(message.Error!, message.MessageId, message.TraceId, message.Timestamp,
                message.CorrelationId, message.CausationId, headers)
            : FlowMessage.Restore(message.Value!, message.MessageId, message.TraceId, message.Timestamp,
                message.CorrelationId, message.CausationId, headers);
        // Observations never apply backpressure to runtime processing. Rejection is possible at capacity.
        _events.Post(observation);
    }

    internal void Complete() => _events.Complete();

    public async ValueTask DisposeAsync()
    {
        Complete();
        await _events.Completion.ConfigureAwait(false);
    }
}
