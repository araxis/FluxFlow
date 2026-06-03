namespace FluxFlow.Components.Journal.Contracts;

public sealed record JournalQueryResult
{
    public required IReadOnlyList<JournalRecord> Records { get; init; }
    public required int TotalMatched { get; init; }
    public required int Offset { get; init; }
    public int? Limit { get; init; }
    public required bool HasMore { get; init; }
}
