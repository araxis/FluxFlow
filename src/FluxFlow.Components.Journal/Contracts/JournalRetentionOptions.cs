namespace FluxFlow.Components.Journal.Contracts;

public sealed record JournalRetentionOptions
{
    public DateTimeOffset? DeleteBefore { get; init; }
    public TimeSpan? MaxAge { get; init; }
    public DateTimeOffset? ReferenceTime { get; init; }
    public int? MaxRecords { get; init; }
}
