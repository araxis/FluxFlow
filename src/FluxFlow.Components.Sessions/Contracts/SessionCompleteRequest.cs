namespace FluxFlow.Components.Sessions.Contracts;

public sealed record SessionCompleteRequest
{
    public required SessionMetadata Session { get; init; }
    public required DateTimeOffset EndedAt { get; init; }
    public required long MessageCount { get; init; }
}
