namespace FluxFlow.Components.Http.Contracts;

public interface IHttpRequestSenderFactory
{
    IHttpRequestSender Create(HttpRequestSenderContext context);
}
