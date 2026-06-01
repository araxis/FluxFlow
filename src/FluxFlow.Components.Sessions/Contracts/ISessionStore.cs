namespace FluxFlow.Components.Sessions.Contracts;

public interface ISessionStore
{
    Task<SessionMetadata?> GetSessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default);

    Task<SessionMetadata> StartSessionAsync(
        SessionStartRequest request,
        CancellationToken cancellationToken = default);

    Task<SessionRecord> AppendMessageAsync(
        SessionAppendRequest request,
        CancellationToken cancellationToken = default);

    Task<SessionMetadata> CompleteSessionAsync(
        SessionCompleteRequest request,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<SessionRecord> ReadMessagesAsync(
        SessionReadRequest request,
        CancellationToken cancellationToken = default);
}
