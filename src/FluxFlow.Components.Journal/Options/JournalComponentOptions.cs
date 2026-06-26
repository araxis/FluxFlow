using FluxFlow.Components.Journal.Contracts;

namespace FluxFlow.Components.Journal.Options;

public sealed class JournalComponentOptions
{
    private IJournalStoreFactory _storeFactory = new MissingJournalStoreFactory();
    private TimeProvider _clock = TimeProvider.System;

    public IJournalStoreFactory StoreFactory => _storeFactory;

    public TimeProvider Clock => _clock;

    public JournalComponentOptions UseStoreFactory(IJournalStoreFactory storeFactory)
    {
        _storeFactory = storeFactory ?? throw new ArgumentNullException(nameof(storeFactory));
        return this;
    }

    public JournalComponentOptions UseStore(
        Func<JournalStoreContext, CancellationToken, ValueTask<JournalStoreLease>> open)
    {
        ArgumentNullException.ThrowIfNull(open);
        _storeFactory = new DelegateJournalStoreFactory(open);
        return this;
    }

    public JournalComponentOptions UseSharedStore(Func<JournalStoreContext, IJournalStore> create)
    {
        ArgumentNullException.ThrowIfNull(create);
        return UseStore(
            (context, _) =>
            {
                var store = create(context);
                if (store is null)
                {
                    throw new InvalidOperationException(
                        "Shared journal store factory returned null.");
                }

                return ValueTask.FromResult(JournalStoreLease.Shared(store));
            });
    }

    public JournalComponentOptions UseSharedStore(IJournalStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        return UseStore(
            (_, _) => ValueTask.FromResult(JournalStoreLease.Shared(store)));
    }

    public JournalComponentOptions UseClock(TimeProvider clock)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        return this;
    }

    private sealed class DelegateJournalStoreFactory(
        Func<JournalStoreContext, CancellationToken, ValueTask<JournalStoreLease>> open)
        : IJournalStoreFactory
    {
        public async ValueTask<JournalStoreLease> OpenAsync(
            JournalStoreContext context,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(context);
            cancellationToken.ThrowIfCancellationRequested();

            var lease = await open(context, cancellationToken).ConfigureAwait(false);
            return lease ?? throw new InvalidOperationException(
                "Journal store factory delegate returned a null lease.");
        }
    }

    private sealed class MissingJournalStoreFactory : IJournalStoreFactory
    {
        public ValueTask<JournalStoreLease> OpenAsync(
            JournalStoreContext context,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException(
                "Journal components require a journal store. Register one through JournalComponentOptions.");
    }
}
