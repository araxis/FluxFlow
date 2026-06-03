namespace FluxFlow.Components.Journal.Contracts;

public sealed record JournalRecord
{
    public required string Id { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public string? Type { get; init; }
    public string? Status { get; init; }
    public string? Source { get; init; }
    public string? WorkflowId { get; init; }
    public string? WorkflowName { get; init; }
    public string? NodeId { get; init; }
    public string? ComponentId { get; init; }
    public string? Subject { get; init; }
    public string? Channel { get; init; }
    public string? Severity { get; init; }
    public string? Level { get; init; }
    public string? Summary { get; init; }
    public int? PayloadBytes { get; init; }
    public string? PayloadPreview { get; init; }
    public IReadOnlyDictionary<string, string> Attributes { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}
