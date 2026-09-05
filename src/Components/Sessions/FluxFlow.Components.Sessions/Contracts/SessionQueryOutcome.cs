namespace FluxFlow.Components.Sessions.Contracts;

public sealed record SessionQueryOutcome
{
    private IReadOnlyList<SessionMetadata> _sessions = Array.Empty<SessionMetadata>();

    public required int Count { get; init; }

    public IReadOnlyList<SessionMetadata> Sessions
    {
        get => _sessions;
        init => _sessions = SessionContentContractMap.CopySessions(value);
    }
}
