namespace FluxFlow.Components.Sessions.Contracts;

public sealed record SessionQueryResult
{
    public required DateTimeOffset Timestamp { get; init; }
    public required string Operation { get; init; }
    public required bool Succeeded { get; init; }
    public required int Count { get; init; }
    public IReadOnlyList<SessionMetadata> Sessions { get; init; } = [];
    public string? CorrelationId { get; init; }
    public string? Message { get; init; }
    public Dictionary<string, string> Attributes { get; init; } = [];
}
