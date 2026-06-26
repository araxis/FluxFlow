namespace FluxFlow.Components.Sessions.Contracts;

public interface ISessionStoreFactory
{
    ValueTask<SessionStoreLease> OpenAsync(
        SessionStoreContext context,
        CancellationToken cancellationToken = default);
}
