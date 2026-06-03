namespace FluxFlow.Components.Projections.Contracts;

public sealed record EventProjectionSnapshot
{
    public required DateTimeOffset Timestamp { get; init; }
    public string? Name { get; init; }
    public required long ObservedCount { get; init; }
    public required long MatchedCount { get; init; }
    public required double CurrentRate { get; init; }
    public DateTimeOffset? FirstMatchedAt { get; init; }
    public DateTimeOffset? LastMatchedAt { get; init; }
    public EventSummary? Latest { get; init; }
    public EventFilter Filter { get; init; } = new();
    public Dictionary<string, string> Attributes { get; init; } = [];
}
