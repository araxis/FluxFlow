using System.Collections.Concurrent;
using FluxFlow.Components.Journal.Contracts;

namespace FluxFlow.Components.Journal.Stores;

public sealed class InMemoryJournalStoreFactory : IJournalStoreFactory
{
    private const string DefaultStoreName = "default";
    private readonly ConcurrentDictionary<string, InMemoryJournalStore> stores = new(StringComparer.Ordinal);
    private readonly JournalRetentionOptions? retention;

    public InMemoryJournalStoreFactory()
        : this(retention: null)
    {
    }

    public InMemoryJournalStoreFactory(JournalRetentionOptions? retention)
    {
        this.retention = CopyRetention(retention);
    }

    public ValueTask<JournalStoreLease> OpenAsync(
        JournalStoreContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        var key = context.StoreName ?? DefaultStoreName;
        var store = stores.GetOrAdd(
            key,
            _ => new InMemoryJournalStore(CopyRetention(retention)));

        return ValueTask.FromResult(JournalStoreLease.Shared(store));
    }

    private static JournalRetentionOptions? CopyRetention(JournalRetentionOptions? source)
        => source is null
            ? null
            : source with { };
}
