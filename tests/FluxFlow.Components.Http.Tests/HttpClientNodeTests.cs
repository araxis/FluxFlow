using FluxFlow.Components.Http;
using FluxFlow.Components.Http.Contracts;
using FluxFlow.Engine.Components;
using FluxFlow.Engine.Definitions;
using FluxFlow.Engine.Runtime;
using Shouldly;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Components.Http.Tests;

public sealed class HttpClientNodeTests
{
    [Fact]
    public async Task Request_RoundTripsThroughInjectedClient()
    {
        var handler = new StubHandler((_, _) =>
            Respond(HttpStatusCode.OK, "pong", "text/plain"));
        var node = CreateNode(new HttpClient(handler), new { });
        var output = LinkOutput<HttpResponseOutput>(node, HttpComponentPorts.Output);

        await SendAsync(node, new HttpRequestInput { Method = "GET", Url = "https://example.test/ping" });

        var response = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        response.StatusCode.ShouldBe(200);
        response.Success.ShouldBeTrue();
        response.Body.ShouldBe("pong");
        response.Method.ShouldBe("GET");
        handler.LastRequest!.RequestUri!.ToString().ShouldBe("https://example.test/ping");
    }

    [Fact]
    public async Task Output_FansOutToEveryLinkedConsumer()
    {
        var handler = new StubHandler((_, _) => Respond(HttpStatusCode.OK, "ok", "text/plain"));
        var node = CreateNode(new HttpClient(handler), new { });
        var first = LinkOutput<HttpResponseOutput>(node, HttpComponentPorts.Output);
        var second = LinkOutput<HttpResponseOutput>(node, HttpComponentPorts.Output);

        await SendAsync(node, new HttpRequestInput { Url = "https://example.test/" });

        (await first.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30))).StatusCode.ShouldBe(200);
        (await second.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30))).StatusCode.ShouldBe(200);
    }

    [Fact]
    public async Task PostBody_SendsContentAndContentType()
    {
        var handler = new StubHandler((_, _) => Respond(HttpStatusCode.Created, "", null));
        var node = CreateNode(new HttpClient(handler), new { });
        var output = LinkOutput<HttpResponseOutput>(node, HttpComponentPorts.Output);

        await SendAsync(node, new HttpRequestInput
        {
            Method = "POST",
            Url = "https://example.test/items",
            Body = "{\"id\":1}",
            ContentType = "application/json"
        });

        (await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30))).StatusCode.ShouldBe(201);
        handler.LastRequest!.Method.Method.ShouldBe("POST");
        handler.LastBody.ShouldBe("{\"id\":1}");
        handler.LastRequest.Content!.Headers.ContentType!.MediaType.ShouldBe("application/json");
    }

    [Fact]
    public async Task RelativeUrl_ResolvesAgainstClientBaseAddress()
    {
        var handler = new StubHandler((_, _) => Respond(HttpStatusCode.OK, "ok", "text/plain"));
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.example.test/") };
        var node = CreateNode(client, new { });
        var output = LinkOutput<HttpResponseOutput>(node, HttpComponentPorts.Output);

        await SendAsync(node, new HttpRequestInput { Url = "v1/status" });

        (await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30))).StatusCode.ShouldBe(200);
        handler.LastRequest!.RequestUri!.ToString().ShouldBe("https://api.example.test/v1/status");
    }

    [Fact]
    public async Task NonSuccessStatus_GoesToOutputByDefault()
    {
        var handler = new StubHandler((_, _) => Respond(HttpStatusCode.InternalServerError, "boom", "text/plain"));
        var node = CreateNode(new HttpClient(handler), new { });
        var output = LinkOutput<HttpResponseOutput>(node, HttpComponentPorts.Output);

        await SendAsync(node, new HttpRequestInput { Url = "https://example.test/" });

        var response = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        response.StatusCode.ShouldBe(500);
        response.Success.ShouldBeFalse();
    }

    [Fact]
    public async Task NonSuccessStatus_GoesToErrorPortWhenConfigured()
    {
        var handler = new StubHandler((_, _) => Respond(HttpStatusCode.NotFound, "nope", "text/plain"));
        var node = CreateNode(new HttpClient(handler), new { treatNonSuccessStatusAsError = true });
        var errors = LinkOutput<HttpErrorOutput>(node, HttpComponentPorts.Errors);

        await SendAsync(node, new HttpRequestInput { Url = "https://example.test/" });

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        error.Kind.ShouldBe(HttpErrorKind.NonSuccessStatus);
        error.StatusCode.ShouldBe(404);
        node.Node.Completion.IsFaulted.ShouldBeFalse();
    }

    [Fact]
    public async Task NetworkFailure_ReportsNetworkErrorAndDoesNotFault()
    {
        var handler = new StubHandler((_, _) =>
            throw new HttpRequestException("connection refused"));
        var node = CreateNode(new HttpClient(handler), new { });
        var errors = LinkOutput<HttpErrorOutput>(node, HttpComponentPorts.Errors);

        await SendAsync(node, new HttpRequestInput { Url = "https://example.test/" });

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        error.Kind.ShouldBe(HttpErrorKind.Network);
        node.Node.Completion.IsFaulted.ShouldBeFalse();
    }

    [Fact]
    public async Task RequestTimeout_ReportsTimeout()
    {
        // The handler blocks until the request token is canceled, so the per-request
        // timeout fires deterministically.
        var handler = new StubHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var node = CreateNode(new HttpClient(handler), new { defaultTimeoutMilliseconds = 100 });
        var errors = LinkOutput<HttpErrorOutput>(node, HttpComponentPorts.Errors);

        await SendAsync(node, new HttpRequestInput { Url = "https://example.test/" });

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        error.Kind.ShouldBe(HttpErrorKind.Timeout);
        node.Node.Completion.IsFaulted.ShouldBeFalse();
    }

    [Fact]
    public async Task MissingUrlWithoutBaseAddress_ReportsInvalidUrl()
    {
        var handler = new StubHandler((_, _) => Respond(HttpStatusCode.OK, "", null));
        var node = CreateNode(new HttpClient(handler), new { });
        var errors = LinkOutput<HttpErrorOutput>(node, HttpComponentPorts.Errors);

        await SendAsync(node, new HttpRequestInput { Url = null });

        (await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30))).Kind.ShouldBe(HttpErrorKind.InvalidUrl);
    }

    [Fact]
    public async Task ResponseBody_TruncatesAtConfiguredCap()
    {
        var payload = new string('x', 5000);
        var handler = new StubHandler((_, _) => Respond(HttpStatusCode.OK, payload, "text/plain"));
        var node = CreateNode(new HttpClient(handler), new { maxResponseBodyBytes = 1000 });
        var output = LinkOutput<HttpResponseOutput>(node, HttpComponentPorts.Output);

        await SendAsync(node, new HttpRequestInput { Url = "https://example.test/" });

        var response = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        response.BodyTruncated.ShouldBeTrue();
        response.BodyBytes.Length.ShouldBe(1000);
    }

    [Fact]
    public void Registration_ExposesOnlyHttpClientType()
    {
        var registry = new RuntimeNodeFactoryRegistry()
            .RegisterHttpComponents(options => options.UseHttpClient(new HttpClient()));

        registry.TryGetFactory(HttpComponentTypes.Client, out _).ShouldBeTrue();
    }

    [Fact]
    public void Factory_WithoutConfiguredHttpClient_Throws()
    {
        var registry = new RuntimeNodeFactoryRegistry().RegisterHttpComponents();
        registry.TryGetFactory(HttpComponentTypes.Client, out var factory).ShouldBeTrue();

        var exception = Should.Throw<InvalidOperationException>(
            () => factory(CreateContext(new { })));
        exception.Message.ShouldContain("UseHttpClient");
    }

    private static RuntimeNode CreateNode(HttpClient client, object configuration)
    {
        var registry = new RuntimeNodeFactoryRegistry()
            .RegisterHttpComponents(options => options.UseHttpClient(client));
        registry.TryGetFactory(HttpComponentTypes.Client, out var factory).ShouldBeTrue();
        return factory(CreateContext(configuration));
    }

    private static RuntimeNodeFactoryContext CreateContext(object configuration)
        => new(
            new NodeName("http"),
            new NodeDefinition
            {
                Type = HttpComponentTypes.Client,
                Configuration = ToConfiguration(configuration)
            },
            "main",
            new Dictionary<NodeName, RuntimeNode>());

    private static Dictionary<string, JsonElement> ToConfiguration(object configuration)
    {
        var root = JsonSerializer.SerializeToElement(configuration);
        return root.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.Clone());
    }

    private static async Task SendAsync(RuntimeNode node, HttpRequestInput input)
    {
        var target = node.FindInput(new PortName(HttpComponentPorts.Input))
            .ShouldBeOfType<InputPort<HttpRequestInput>>();
        await target.Target.SendAsync(input).WaitAsync(TimeSpan.FromSeconds(30));
    }

    private static BufferBlock<T> LinkOutput<T>(RuntimeNode node, string portName)
    {
        var target = new BufferBlock<T>();
        node.FindOutput(new PortName(portName))!
            .TryLinkTo(
                new InputPort<T>(
                    new PortAddress("test", new NodeName("sink"), new PortName("Input")),
                    target),
                propagateCompletion: false,
                out var error);
        error.ShouldBeNull();
        return target;
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
