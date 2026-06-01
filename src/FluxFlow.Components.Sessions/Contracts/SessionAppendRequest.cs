namespace FluxFlow.Components.Sessions.Contracts;

public sealed record SessionAppendRequest
{
    public required SessionMetadata Session { get; init; }
    public required SessionRecordInput Input { get; init; }
    public required long Sequence { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
}
