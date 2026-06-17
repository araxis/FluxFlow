using FluxFlow.Components.Http.Contracts;

namespace FluxFlow.Components.Http.Options;

public sealed class HttpComponentOptions
{
    private IHttpRequestSenderFactory _requestSenderFactory =
        new HttpClientRequestSenderFactory();
    private TimeProvider _clock = TimeProvider.System;

    public IHttpRequestSenderFactory RequestSenderFactory => _requestSenderFactory;

    public TimeProvider Clock => _clock;

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
        _requestSenderFactory = new DelegateHttpRequestSenderFactory(create, null);
        return this;
    }

    public HttpComponentOptions UseRequestSender(
        Func<HttpClientSenderContext, IHttpRequestSender> createClient)
    {
        ArgumentNullException.ThrowIfNull(createClient);
        _requestSenderFactory = new DelegateHttpRequestSenderFactory(null, createClient);
        return this;
    }

    public HttpComponentOptions UseClock(TimeProvider clock)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        return this;
    }

    private sealed class DelegateHttpRequestSenderFactory(
        Func<HttpRequestSenderContext, IHttpRequestSender>? create,
        Func<HttpClientSenderContext, IHttpRequestSender>? createClient)
        : IHttpRequestSenderFactory
    {
        public IHttpRequestSender Create(HttpRequestSenderContext context)
            => create is not null
                ? create(context)
                : throw new InvalidOperationException(
                    "This sender factory is configured for client-scoped senders only.");

        public IHttpRequestSender CreateClient(HttpClientSenderContext context)
            => createClient is not null
                ? createClient(context)
                : throw new InvalidOperationException(
                    "This sender factory is configured for request-scoped senders only.");
    }
}
