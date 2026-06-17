namespace FluxFlow.Components.Http.Contracts;

public interface IHttpRequestSenderFactory
{
    /// <summary>
    /// Builds a request-scoped sender. Retained for any per-request needs.
    /// </summary>
    IHttpRequestSender Create(HttpRequestSenderContext context);

    /// <summary>
    /// Builds a client-scoped sender at connect-time from the owning
    /// http.client handle's configuration. The sender is shared by request
    /// nodes that borrow it at call-time and is disposed at disconnect-time.
    /// </summary>
    IHttpRequestSender CreateClient(HttpClientSenderContext context);
}
