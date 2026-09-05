using FluxFlow.Composition.Addressing;
using FluxFlow.Nodes;

namespace FluxFlow.Engine.Ports;

public enum ApplicationPortDirection
{
    Input = 1,
    Output = 2
}

public enum ApplicationPortKind
{
    Message = 0,
    Signal = 1
}

public sealed record ApplicationPortMetadata
{
    public required ApplicationAddress Address { get; init; }

    public required ApplicationPortDirection Direction { get; init; }

    public ApplicationPortKind Kind { get; init; }

    public required Type PayloadType { get; init; }

    public required int Capacity { get; init; }
}

public enum PortSendStatus
{
    Accepted = 1,
    Full = 2,
    Unavailable = 3,
    Completed = 4
}

public sealed record PortSendResult
{
    public required ApplicationAddress Port { get; init; }

    public required PortSendStatus Status { get; init; }

    public bool IsAccepted => Status == PortSendStatus.Accepted;
}

public enum PortReceiveStatus
{
    Received = 1,
    Unavailable = 2,
    Completed = 3,
    TimedOut = 4
}

public sealed record PortReceiveResult<T>
{
    public required ApplicationAddress Port { get; init; }

    public required PortReceiveStatus Status { get; init; }

    public FlowMessage<T>? Message { get; init; }

    public bool HasMessage => Status == PortReceiveStatus.Received;
}

public enum PortObserveStatus
{
    Started = 1,
    Unavailable = 2,
    Completed = 3
}

public sealed record PortObserveResult<T>
{
    public required ApplicationAddress Port { get; init; }

    public required PortObserveStatus Status { get; init; }

    public PortObservation<T>? Observation { get; init; }
}

public enum PortRequestStatus
{
    Received = 1,
    InputFull = 2,
    InputUnavailable = 3,
    InputCompleted = 4,
    OutputUnavailable = 5,
    OutputCompleted = 6,
    TimedOut = 7
}

public sealed record PortRequestResult<T>
{
    public required ApplicationAddress InputPort { get; init; }

    public required ApplicationAddress OutputPort { get; init; }

    public required PortRequestStatus Status { get; init; }

    public FlowMessage<T>? Response { get; init; }

    public bool HasResponse => Status == PortRequestStatus.Received;
}

public enum ApplicationPortRejectionReason
{
    Full = 1,
    Unavailable = 2,
    Completed = 3,
    ConditionFailed = 4,
    TargetRejected = 5,
    ObservationOverflowed = 6,
    SourceFaulted = 7,
    ComponentFaulted = 8,
    OutputCaptureFailed = 9
}

public sealed record ApplicationPortRejection
{
    public required DateTimeOffset Timestamp { get; init; }

    public required ApplicationAddress Port { get; init; }

    public ApplicationAddress? RelatedPort { get; init; }

    public CorrelationId? CorrelationId { get; init; }

    public TraceId? TraceId { get; init; }

    public MessageId? MessageId { get; init; }

    public required ApplicationPortRejectionReason Reason { get; init; }

    public Exception? Exception { get; init; }
}
