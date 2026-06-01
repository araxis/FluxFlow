namespace FluxFlow.Components.Sessions.Contracts;

public sealed record SessionStartRequest
{
    public string? SessionId { get; init; }
    public string? Name { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
    public string? Notes { get; init; }
    public Dictionary<string, string> Tags { get; init; } = [];
}
