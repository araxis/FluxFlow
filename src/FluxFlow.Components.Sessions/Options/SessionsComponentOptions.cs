using FluxFlow.Components.Sessions.Contracts;

namespace FluxFlow.Components.Sessions.Options;

public sealed class SessionsComponentOptions
{
    private ISessionStoreFactory _storeFactory = new MissingSessionStoreFactory();
    private TimeProvider _clock = TimeProvider.System;

    public ISessionStoreFactory StoreFactory => _storeFactory;

    public TimeProvider Clock => _clock;

    public SessionsComponentOptions UseStoreFactory(ISessionStoreFactory storeFactory)
    {
        _storeFactory = storeFactory ?? throw new ArgumentNullException(nameof(storeFactory));
        return this;
    }

    public SessionsComponentOptions UseStore(Func<SessionStoreContext, ISessionStore> create)
    {
        ArgumentNullException.ThrowIfNull(create);
        _storeFactory = new DelegateSessionStoreFactory(create);
        return this;
    }

    public SessionsComponentOptions UseClock(TimeProvider clock)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        return this;
    }

    private sealed class DelegateSessionStoreFactory(
        Func<SessionStoreContext, ISessionStore> create)
        : ISessionStoreFactory
    {
        public ISessionStore Create(SessionStoreContext context)
            => create(context);
    }

    private sealed class MissingSessionStoreFactory : ISessionStoreFactory
    {
        public ISessionStore Create(SessionStoreContext context)
            => throw new InvalidOperationException(
                "Session components require a session store. Register one through SessionsComponentOptions.");
    }
}
