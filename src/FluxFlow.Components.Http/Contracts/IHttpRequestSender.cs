namespace FluxFlow.Components.Http.Contracts;

public interface IHttpRequestSender : IAsyncDisposable
{
    Task<HttpResponseOutput> SendAsync(
        HttpRequestSendContext context,
        CancellationToken cancellationToken = default);
}
