namespace FluxFlow.Components.Journal.Contracts;

public interface IJournalStoreFactory
{
    ValueTask<JournalStoreLease> OpenAsync(
        JournalStoreContext context,
        CancellationToken cancellationToken = default);
}
