namespace FluxFlow.Components.Sessions.Contracts;

public sealed record SessionCompleteRequest
{
    private SessionMetadata _session = null!;

    public required SessionMetadata Session
    {
        get => _session;
        init => _session = SessionContractNormalization.CopySession(value)!;
    }

    public required DateTimeOffset EndedAt { get; init; }
    public required long MessageCount { get; init; }
}
