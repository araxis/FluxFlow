namespace FluxFlow.Components.Sessions.Contracts;

public sealed record SessionMetadata
{
    public required string SessionId { get; init; }
    public string? Name { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? EndedAt { get; init; }
    public long MessageCount { get; init; }
    public string? Notes { get; init; }
    public Dictionary<string, string> Tags { get; init; } = [];
}
