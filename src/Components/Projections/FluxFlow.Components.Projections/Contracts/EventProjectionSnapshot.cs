namespace FluxFlow.Components.Projections.Contracts;

public sealed record EventProjectionSnapshot
{
    private IReadOnlyDictionary<string, string> _attributes =
        ProjectionContractNormalization.CopyAttributes(null);

    public required DateTimeOffset Timestamp { get; init; }
    public string? Name { get; init; }
    public required long ObservedCount { get; init; }
    public required long MatchedCount { get; init; }
    public required double CurrentRate { get; init; }
    public DateTimeOffset? FirstMatchedAt { get; init; }
    public DateTimeOffset? LastMatchedAt { get; init; }
    public EventSummary? Latest { get; init; }
    public EventFilter Filter { get; init; } = new();
    public IReadOnlyDictionary<string, string> Attributes
    {
        get => _attributes;
        init => _attributes = ProjectionContractNormalization.CopyAttributes(value);
    }
}
