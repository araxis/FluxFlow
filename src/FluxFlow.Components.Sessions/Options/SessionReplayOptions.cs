using FluxFlow.Components.Sessions.Contracts;

namespace FluxFlow.Components.Sessions.Options;

public sealed record SessionReplayOptions
{
    private string? _store;
    private string? _sessionId;
    private SessionReplayMode _mode = SessionReplayMode.Instant;
    private int _boundedCapacity = 128;
    private long? _startSequence;
    private int? _maxMessages;
    private double _fixedIntervalMilliseconds = 1000;
    private double _speedMultiplier = 1;

    public string? Store
    {
        get => _store;
        init => _store = SessionOptionValidation.Normalize(value);
    }

    public string? SessionId
    {
        get => _sessionId;
        init => _sessionId = SessionOptionValidation.Normalize(value);
    }

    public SessionReplayMode Mode
    {
        get => _mode;
        init => _mode = SessionOptionValidation.ValidateReplayMode(value);
    }

    public int BoundedCapacity
    {
        get => _boundedCapacity;
        init => _boundedCapacity = SessionOptionValidation.ValidateBoundedCapacity(value);
    }

    public long? StartSequence
    {
        get => _startSequence;
        init => _startSequence = SessionOptionValidation.ValidateStartSequence(value);
    }

    public int? MaxMessages
    {
        get => _maxMessages;
        init => _maxMessages = SessionOptionValidation.ValidateMaxMessages(value);
    }

    public double FixedIntervalMilliseconds
    {
        get => _fixedIntervalMilliseconds;
        init => _fixedIntervalMilliseconds =
            SessionOptionValidation.ValidateFixedIntervalMilliseconds(value);
    }

    public double SpeedMultiplier
    {
        get => _speedMultiplier;
        init => _speedMultiplier = SessionOptionValidation.ValidateSpeedMultiplier(value);
    }
}
