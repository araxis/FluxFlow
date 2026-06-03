namespace FluxFlow.Components.Journal.Contracts;

public interface IJournalStore
{
    ValueTask<JournalAppendResult> AppendAsync(
        JournalRecord record,
        CancellationToken cancellationToken = default);

    ValueTask<JournalQueryResult> QueryAsync(
        JournalQuery query,
        CancellationToken cancellationToken = default);

    ValueTask<JournalPruneResult> PruneAsync(
        JournalRetentionOptions options,
        CancellationToken cancellationToken = default);
}
