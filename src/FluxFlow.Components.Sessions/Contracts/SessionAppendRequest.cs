namespace FluxFlow.Components.Sessions.Contracts;

public sealed record SessionAppendRequest
{
    private SessionMetadata _session = null!;
    private SessionRecordInput _input = null!;

    public required SessionMetadata Session
    {
        get => _session;
        init => _session = SessionContractNormalization.CopySession(value)!;
    }

    public required SessionRecordInput Input
    {
        get => _input;
        init => _input = SessionContractNormalization.CopyInput(value)!;
    }

    public required long Sequence { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
}
