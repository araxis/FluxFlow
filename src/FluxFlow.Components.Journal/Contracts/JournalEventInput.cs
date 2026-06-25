namespace FluxFlow.Components.Journal.Contracts;

public sealed record JournalEventInput
{
    public required DateTimeOffset Timestamp { get; init; }
    public string? Type { get; init; }
    public string? Status { get; init; }
    public string? Source { get; init; }
    public string? SourceNodeId { get; init; }
    public string? Subject { get; init; }
    public string? Channel { get; init; }
    public int? PayloadBytes { get; init; }
    public string? PayloadPreview { get; init; }
    public IReadOnlyDictionary<string, string> Attributes { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}
