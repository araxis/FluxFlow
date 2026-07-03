using FluxFlow.Components.Sessions.Contracts;

namespace FluxFlow.Components.Sessions.Options;

public sealed class SessionComponentOptions
{
    private ISessionStoreFactory _storeFactory = new MissingSessionStoreFactory();
    private TimeProvider _clock = TimeProvider.System;

    public ISessionStoreFactory StoreFactory => _storeFactory;

    public TimeProvider Clock => _clock;

    public SessionComponentOptions UseStoreFactory(ISessionStoreFactory storeFactory)
    {
        _storeFactory = storeFactory ?? throw new ArgumentNullException(nameof(storeFactory));
        return this;
    }

    public SessionComponentOptions UseStore(
        Func<SessionStoreContext, CancellationToken, ValueTask<SessionStoreLease>> open)
    {
        ArgumentNullException.ThrowIfNull(open);
        _storeFactory = new DelegateSessionStoreFactory(open);
        return this;
    }

    public SessionComponentOptions UseSharedStore(Func<SessionStoreContext, ISessionStore> create)
    {
        ArgumentNullException.ThrowIfNull(create);
        return UseStore(
            (context, _) =>
            {
                var store = create(context);
                if (store is null)
                {
                    throw new InvalidOperationException(
                        "Shared session store factory returned null.");
                }

                return ValueTask.FromResult(SessionStoreLease.Shared(store));
            });
    }

    public SessionComponentOptions UseSharedStore(ISessionStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        return UseStore(
            (_, _) => ValueTask.FromResult(SessionStoreLease.Shared(store)));
    }

    public SessionComponentOptions UseClock(TimeProvider clock)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        return this;
    }

    private sealed class DelegateSessionStoreFactory(
        Func<SessionStoreContext, CancellationToken, ValueTask<SessionStoreLease>> open)
        : ISessionStoreFactory
    {
        public async ValueTask<SessionStoreLease> OpenAsync(
            SessionStoreContext context,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(context);
            cancellationToken.ThrowIfCancellationRequested();

            var lease = await open(context, cancellationToken).ConfigureAwait(false);
            return lease ?? throw new InvalidOperationException(
                "Session store factory delegate returned a null lease.");
        }
    }

    private sealed class MissingSessionStoreFactory : ISessionStoreFactory
    {
        public ValueTask<SessionStoreLease> OpenAsync(
            SessionStoreContext context,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException(
                "Session components require a session store. Register one through SessionComponentOptions.");
    }
}
