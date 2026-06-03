namespace FluxFlow.Components.Journal.Contracts;

public sealed record JournalAppendResult
{
    public required JournalRecord Record { get; init; }
    public required long Position { get; init; }
}
