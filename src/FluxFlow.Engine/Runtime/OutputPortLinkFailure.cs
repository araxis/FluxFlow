using FluxFlow.Engine.Definitions;

namespace FluxFlow.Engine.Runtime;

/// <summary>
/// Describes a per-link delivery failure on an output port. Condition failures
/// drop the affected message for that link only; rejected targets detach the
/// link so sibling consumers keep receiving messages.
/// </summary>
public sealed record OutputPortLinkFailure
{
    public required PortAddress Port { get; init; }
    public required OutputPortLinkFailureReason Reason { get; init; }
    public Exception? Exception { get; init; }
}

public enum OutputPortLinkFailureReason
{
    /// <summary>A conditional link predicate threw while evaluating a message.</summary>
    ConditionFailed = 1,

    /// <summary>A linked input declined a message because it already completed or faulted.</summary>
    TargetRejected = 2
}
