using FluxFlow.Components.Http.Contracts;

namespace FluxFlow.Components.Http.Options;

public sealed class HttpComponentOptions
{
    private IHttpRequestSenderFactory _requestSenderFactory =
        new HttpClientRequestSenderFactory();

    public IHttpRequestSenderFactory RequestSenderFactory => _requestSenderFactory;

    public HttpComponentOptions UseRequestSenderFactory(
        IHttpRequestSenderFactory requestSenderFactory)
    {
        _requestSenderFactory = requestSenderFactory
            ?? throw new ArgumentNullException(nameof(requestSenderFactory));
        return this;
    }

    public HttpComponentOptions UseRequestSender(
        Func<HttpRequestSenderContext, IHttpRequestSender> create)
    {
        ArgumentNullException.ThrowIfNull(create);
        _requestSenderFactory = new DelegateHttpRequestSenderFactory(create);
        return this;
    }

    private sealed class DelegateHttpRequestSenderFactory(
        Func<HttpRequestSenderContext, IHttpRequestSender> create)
        : IHttpRequestSenderFactory
    {
        public IHttpRequestSender Create(HttpRequestSenderContext context)
            => create(context);
    }
}
