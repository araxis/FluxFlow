using FluxFlow.Components.Sessions.Contracts;

namespace FluxFlow.Components.Sessions.Options;

public sealed record SessionReplayOptions
{
    public string? Store { get; init; }
    public string? SessionId { get; init; }
    public SessionReplayMode Mode { get; init; } = SessionReplayMode.Instant;
    public int BoundedCapacity { get; init; } = 128;
    public long? StartSequence { get; init; }
    public int? MaxMessages { get; init; }
    public double FixedIntervalMilliseconds { get; init; } = 1000;
    public double SpeedMultiplier { get; init; } = 1;
}
