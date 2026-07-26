using FluxFlow.Composition.Addressing;
using FluxFlow.Nodes;

namespace FluxFlow.Engine.Ports;

internal enum ApplicationPortActivityKind
{
    InputAccepted,
    OutputEmitted
}

internal sealed record ApplicationPortActivity(
    DateTimeOffset Timestamp,
    ApplicationPortActivityKind Kind,
    ApplicationAddress Port,
    ApplicationAddress? RelatedPort,
    CorrelationId? CorrelationId,
    TraceId TraceId,
    MessageId MessageId);
