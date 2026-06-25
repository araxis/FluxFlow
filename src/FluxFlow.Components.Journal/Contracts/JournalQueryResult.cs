namespace FluxFlow.Components.Journal.Contracts;

public sealed record JournalQueryResult
{
    private IReadOnlyList<JournalRecord> _records = [];

    public required IReadOnlyList<JournalRecord> Records
    {
        get => _records;
        init => _records = JournalContractNormalization.CopyRecords(value);
    }

    public required int TotalMatched { get; init; }
    public required int Offset { get; init; }
    public int? Limit { get; init; }
    public required bool HasMore { get; init; }
}
