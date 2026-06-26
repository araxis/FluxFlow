using FluxFlow.Components.Http;
using FluxFlow.Components.Http.Contracts;
using FluxFlow.Components.Http.Nodes;
using FluxFlow.Components.Http.Options;
using FluxFlow.Nodes;
using Shouldly;
using System.Net;
using System.Text;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Components.Http.Tests;

// Every test news the node directly — no engine, no registry. Messages travel as
// FlowMessage<T> envelopes; the correlation id flows request -> response for free.
public sealed class HttpClientNodeTests
{
    [Fact]
    public async Task Request_RoundTripsAndPreservesCorrelationId()
    {
        var handler = new StubHandler((_, _) => Respond(HttpStatusCode.OK, "pong", "text/plain"));
        await using var node = new HttpClientNode(new HttpClient(handler));
        var output = Sink(node.Output);

        var request = FlowMessage.Create(new HttpRequestInput { Method = "GET", Url = "https://example.test/ping" });
        await node.Input.SendAsync(request);

        var response = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        response.CorrelationId.ShouldBe(request.CorrelationId);   // the whole point of the envelope
        response.Payload.StatusCode.ShouldBe(200);
        response.Payload.Success.ShouldBeTrue();
        response.Payload.Body.ShouldBe("pong");
        handler.LastRequest!.RequestUri!.ToString().ShouldBe("https://example.test/ping");
    }

    [Fact]
    public async Task Output_FansOutEveryResponseToEveryConsumer()
    {
        // The usual workflow case, with NO engine: one node's output linked to two
        // downstream consumers (a "logger" and a "mapper"). Both see every response.
        var responses = 0;
        var handler = new StubHandler((_, _) =>
            Respond(HttpStatusCode.OK, $"r{Interlocked.Increment(ref responses)}", "text/plain"));
        await using var node = new HttpClientNode(new HttpClient(handler));
        var logger = Sink(node.Output);
        var mapper = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(new HttpRequestInput { Url = "https://example.test/1" }));
        await node.Input.SendAsync(FlowMessage.Create(new HttpRequestInput { Url = "https://example.test/2" }));

        (await logger.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30))).Payload.Body.ShouldBe("r1");
        (await logger.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30))).Payload.Body.ShouldBe("r2");
        (await mapper.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30))).Payload.Body.ShouldBe("r1");
        (await mapper.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30))).Payload.Body.ShouldBe("r2");
    }

    [Fact]
    public async Task PostBody_SendsContentAndContentType()
    {
        var handler = new StubHandler((_, _) => Respond(HttpStatusCode.Created, "", null));
        await using var node = new HttpClientNode(new HttpClient(handler));
        var output = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(new HttpRequestInput
        {
            Method = "POST",
            Url = "https://example.test/items",
            Body = "{\"id\":1}",
            ContentType = "application/json"
        }));

        (await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30))).Payload.StatusCode.ShouldBe(201);
        handler.LastRequest!.Method.Method.ShouldBe("POST");
        handler.LastBody.ShouldBe("{\"id\":1}");
        handler.LastRequest.Content!.Headers.ContentType!.MediaType.ShouldBe("application/json");
    }

    [Fact]
    public async Task RelativeUrl_ResolvesAgainstClientBaseAddress()
    {
        var handler = new StubHandler((_, _) => Respond(HttpStatusCode.OK, "ok", "text/plain"));
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.example.test/") };
        await using var node = new HttpClientNode(client);
        var output = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(new HttpRequestInput { Url = "v1/status" }));

        (await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30))).Payload.StatusCode.ShouldBe(200);
        handler.LastRequest!.RequestUri!.ToString().ShouldBe("https://api.example.test/v1/status");
    }

    [Fact]
    public async Task NonSuccessStatus_GoesToOutputByDefault()
    {
        var handler = new StubHandler((_, _) => Respond(HttpStatusCode.InternalServerError, "boom", "text/plain"));
        await using var node = new HttpClientNode(new HttpClient(handler));
        var output = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(new HttpRequestInput { Url = "https://example.test/" }));

        var response = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        response.Payload.StatusCode.ShouldBe(500);
        response.Payload.Success.ShouldBeFalse();
    }

    [Fact]
    public async Task NonSuccessStatus_GoesToErrorPortWhenConfigured()
    {
        var handler = new StubHandler((_, _) => Respond(HttpStatusCode.NotFound, "nope", "text/plain"));
        await using var node = new HttpClientNode(
            new HttpClient(handler),
            new HttpClientNodeOptions { TreatNonSuccessStatusAsError = true });
        var errors = Sink(node.Errors);

        var request = FlowMessage.Create(new HttpRequestInput { Url = "https://example.test/" });
        await node.Input.SendAsync(request);

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        error.Code.ShouldBe(HttpErrorCodes.NonSuccessStatus);
        error.CorrelationId.ShouldBe(request.CorrelationId);
        error.Context!.ShouldContain("statusCode=404");
        node.Completion.IsFaulted.ShouldBeFalse();
    }

    [Fact]
    public async Task NetworkFailure_ReportsNetworkErrorAndDoesNotFault()
    {
        var handler = new StubHandler((_, _) => throw new HttpRequestException("connection refused"));
        await using var node = new HttpClientNode(new HttpClient(handler));
        var errors = Sink(node.Errors);

        var request = FlowMessage.Create(new HttpRequestInput { Url = "https://example.test/" });
        await node.Input.SendAsync(request);

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        error.Code.ShouldBe(HttpErrorCodes.Network);
        error.CorrelationId.ShouldBe(request.CorrelationId);

        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));
        node.Completion.IsFaulted.ShouldBeFalse();
    }

    [Fact]
    public async Task RequestTimeout_ReportsTimeout()
    {
        var handler = new StubHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        await using var node = new HttpClientNode(
            new HttpClient(handler),
            new HttpClientNodeOptions { DefaultTimeoutMilliseconds = 100 });
        var errors = Sink(node.Errors);

        await node.Input.SendAsync(FlowMessage.Create(new HttpRequestInput { Url = "https://example.test/" }));

        (await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30))).Code.ShouldBe(HttpErrorCodes.Timeout);
    }

    [Fact]
    public async Task MissingUrlWithoutBaseAddress_ReportsInvalidUrl()
    {
        var handler = new StubHandler((_, _) => Respond(HttpStatusCode.OK, "", null));
        await using var node = new HttpClientNode(new HttpClient(handler));
        var errors = Sink(node.Errors);

        await node.Input.SendAsync(FlowMessage.Create(new HttpRequestInput { Url = null }));

        (await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30))).Code.ShouldBe(HttpErrorCodes.InvalidUrl);
    }

    [Fact]
    public async Task ResponseBody_TruncatesAtConfiguredCap()
    {
        var payload = new string('x', 5000);
        var handler = new StubHandler((_, _) => Respond(HttpStatusCode.OK, payload, "text/plain"));
        await using var node = new HttpClientNode(
            new HttpClient(handler),
            new HttpClientNodeOptions { MaxResponseBodyBytes = 1000 });
        var output = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(new HttpRequestInput { Url = "https://example.test/" }));

        var response = (await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30))).Payload;
        response.BodyTruncated.ShouldBeTrue();
        response.BodyBytes.Length.ShouldBe(1000);
    }

    [Fact]
    public async Task Success_EmitsEventCarryingCorrelationId()
    {
        var handler = new StubHandler((_, _) => Respond(HttpStatusCode.OK, "ok", "text/plain"));
        await using var node = new HttpClientNode(new HttpClient(handler));
        Sink(node.Output);
        var events = Sink(node.Events);

        var request = FlowMessage.Create(new HttpRequestInput { Url = "https://example.test/" });
        await node.Input.SendAsync(request);

        var @event = await events.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        @event.Name.ShouldBe(HttpClientNode.RequestSucceeded);
        @event.Level.ShouldBe(FlowEventLevel.Information);
        @event.CorrelationId.ShouldBe(request.CorrelationId);
    }

    [Fact]
    public void Constructor_RequiresHttpClient()
        => Should.Throw<ArgumentNullException>(() => new HttpClientNode(null!));

    [Fact]
    public void Constructor_RejectsInvalidOptions()
    {
        AssertInvalidOptions(
            new HttpClientNodeOptions { BoundedCapacity = 0 },
            "BoundedCapacity");
        AssertInvalidOptions(
            new HttpClientNodeOptions { MaxResponseBodyBytes = 0 },
            "MaxResponseBodyBytes");
        AssertInvalidOptions(
            new HttpClientNodeOptions { MaxDegreeOfParallelism = 0 },
            "MaxDegreeOfParallelism");
        AssertInvalidOptions(
            new HttpClientNodeOptions { DefaultTimeoutMilliseconds = 0 },
            "DefaultTimeoutMilliseconds");
    }

    private static void AssertInvalidOptions(
        HttpClientNodeOptions options,
        string expectedMessage)
    {
        using var client = new HttpClient(new StubHandler((_, _) =>
            Respond(HttpStatusCode.OK, "", null)));

        var exception = Should.Throw<ArgumentOutOfRangeException>(
            () => new HttpClientNode(client, options));

        exception.Message.ShouldContain(expectedMessage);
    }

    private static BufferBlock<T> Sink<T>(ISourceBlock<T> source)
    {
        var sink = new BufferBlock<T>();
        source.LinkTo(sink, new DataflowLinkOptions { PropagateCompletion = true });
        return sink;
    }

    private static Task<HttpResponseMessage> Respond(HttpStatusCode status, string body, string? contentType)
    {
        var response = new HttpResponseMessage(status)
        {
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes(body))
        };
        if (contentType is not null)
        {
            response.Content.Headers.ContentType =
                new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        }

        return Task.FromResult(response);
    }

    private sealed class StubHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            if (request.Content is not null)
            {
                LastBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            var response = await handler(request, cancellationToken);
            response.RequestMessage ??= request;
            return response;
        }
    }
}
