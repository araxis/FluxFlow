namespace FluxFlow.Components.Journal.Contracts;

public sealed record JournalPruneResult
{
    public required int Removed { get; init; }
    public required int Remaining { get; init; }
    public DateTimeOffset? DeleteBefore { get; init; }
    public int? MaxRecords { get; init; }
}
