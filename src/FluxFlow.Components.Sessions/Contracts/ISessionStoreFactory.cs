namespace FluxFlow.Components.Sessions.Contracts;

public interface ISessionStoreFactory
{
    ISessionStore Create(SessionStoreContext context);
}
