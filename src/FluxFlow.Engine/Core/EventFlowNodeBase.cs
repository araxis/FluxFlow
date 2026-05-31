using FluxFlow.Engine.Core;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Engine.Components;

public abstract class EventFlowNodeBase : FlowNodeBase, IFlowEventSource
{
    private readonly BroadcastBlock<FlowEvent> _events = new(static flowEvent => flowEvent);

    protected EventFlowNodeBase()
    {
    }

    protected EventFlowNodeBase(FlowNodeId id)
        : base(id)
    {
    }

    public ISourceBlock<FlowEvent> Events => _events;

    protected bool EmitEvent(FlowEvent flowEvent)
    {
        ArgumentNullException.ThrowIfNull(flowEvent);
        return _events.Post(flowEvent);
    }

    protected bool EmitEvent(
        string type,
        string? source = null,
        string? subject = null,
        string? status = null,
        string? topic = null,
        int? payloadBytes = null,
        string? payloadPreview = null,
        IReadOnlyDictionary<string, string>? attributes = null)
        => EmitEvent(new FlowEvent
        {
            Timestamp = DateTimeOffset.UtcNow,
            Type = type,
            Source = string.IsNullOrWhiteSpace(source) ? Id.ToString() : source,
            SourceNodeId = Id,
            Subject = subject,
            Status = status,
            Topic = topic,
            PayloadBytes = payloadBytes,
            PayloadPreview = payloadPreview,
            Attributes = attributes ?? new Dictionary<string, string>(StringComparer.Ordinal)
        });

    protected override void OnNodeCompleted()
        => _events.Complete();

    protected override void OnNodeFaulted(Exception exception)
        => ((IDataflowBlock)_events).Fault(exception);
}
