using FluxFlow.Components.Sessions.Contracts;
using FluxFlow.Components.Sessions.Timing;

namespace FluxFlow.Components.Sessions.Options;

public sealed class SessionsComponentOptions
{
    private ISessionStoreFactory _storeFactory = new MissingSessionStoreFactory();
    private ISessionClock _clock = SystemSessionClock.Instance;

    public ISessionStoreFactory StoreFactory => _storeFactory;

    public ISessionClock Clock => _clock;

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

    public SessionsComponentOptions UseClock(ISessionClock clock)
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
