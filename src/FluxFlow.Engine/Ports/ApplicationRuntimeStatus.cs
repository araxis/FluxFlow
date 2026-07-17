using FluxFlow.Composition.Addressing;

namespace FluxFlow.Engine.Ports;

public enum ApplicationRuntimeState
{
    Active = 1,
    Completing = 2,
    Completed = 3,
    Faulted = 4,
    Disposed = 5
}

public enum ApplicationPortAvailability
{
    Available = 1,
    Unavailable = 2,
    Completed = 3
}

public sealed record ApplicationPortStatus
{
    public required ApplicationAddress Address { get; init; }

    public required ApplicationPortDirection Direction { get; init; }

    public required Type PayloadType { get; init; }

    public required int Capacity { get; init; }

    public required int PendingMessages { get; init; }

    public required int ActiveAttachments { get; init; }

    public required ApplicationPortAvailability Availability { get; init; }
}

public sealed record ApplicationRuntimeStatus
{
    public required ApplicationRuntimeState State { get; init; }

    public required DateTimeOffset ChangedAt { get; init; }

    public required IReadOnlyList<ApplicationPortStatus> Ports { get; init; }
}
