namespace FluxFlow.Coordination;

public enum PendingExchangeStartStatus
{
    Accepted = 0,
    Duplicate = 1,
    CapacityReached = 2,
    Stopped = 3
}

public enum PendingExchangeFeedbackStatus
{
    Resolved = 0,
    Duplicate = 1,
    Late = 2,
    NotFound = 3,
    Stopped = 4
}

public enum PendingExchangeCompletionKind
{
    Resolved = 0,
    TimedOut = 1,
    Cancelled = 2,
    Stopped = 3,
    Faulted = 4
}
