using FluxFlow.Data;
using FluxFlow.Nodes;
using FluxFlow.Composition.Addressing;

namespace FluxFlow.Engine.Signals;

internal static class ApplicationSignalMessage
{
    internal static FlowMessage Create(
        FlowEvent @event,
        CorrelationId? correlationId = null,
        TraceId? traceId = null,
        MessageId? causationId = null,
        ApplicationAddress? source = null)
    {
        ArgumentNullException.ThrowIfNull(@event);
        var canonical = @event.ToMessage();
        var headers = canonical.Headers.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        if (source is not null)
        {
            headers[FlowEventHeaders.Source] = source.Value;
            if (source.Kind == ApplicationAddressKind.WorkflowPort)
            {
                headers[FlowEventHeaders.Workflow] = source.Segments[0];
                headers[FlowEventHeaders.Component] = source.Segments[1];
            }
        }
        return FlowMessage.Restore(
            canonical.Value ?? FlowValue.Null,
            canonical.MessageId,
            traceId is { IsEmpty: false } ? traceId.Value : canonical.TraceId,
            @event.Timestamp,
            correlationId is { IsEmpty: false } ? correlationId : canonical.CorrelationId,
            causationId,
            headers);
    }
}
