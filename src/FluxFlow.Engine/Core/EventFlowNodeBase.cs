using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Engine.Components;

public abstract class EventFlowNodeBase : FlowNodeBase, IFlowEventSource
{
    // Non-lossy multi-consumer fanout (same primitive as Errors/Diagnostics):
    // a slow or late event consumer must not silently miss events.
    private readonly FlowFanoutSource<FlowEvent> _events = new();

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
        string? channel = null,
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
            Channel = channel,
            PayloadBytes = payloadBytes,
            PayloadPreview = payloadPreview,
            // Defensive copy so a caller cannot mutate attributes after emit and
            // corrupt the event observed by other (fanned-out) consumers.
            Attributes = attributes is null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : new Dictionary<string, string>(attributes, StringComparer.Ordinal)
        });

    protected override void OnNodeCompleted()
        => _events.Complete();

    protected override void OnNodeFaulted(Exception exception)
        => _events.Fault(exception);
}
