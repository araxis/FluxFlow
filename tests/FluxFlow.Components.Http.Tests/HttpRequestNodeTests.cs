using FluxFlow.Components.Http.Contracts;
using FluxFlow.Components.Http.Diagnostics;
using FluxFlow.Components.Http.Options;
using FluxFlow.Components.Http.Timing;
using FluxFlow.Engine.Components;
using FluxFlow.Engine.Definitions;
using FluxFlow.Engine.Runtime;
using Shouldly;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Components.Http.Tests;

public sealed class HttpRequestNodeTests
{
    [Fact]
    public async Task Request_SendsViaConfiguredSenderAndEmitsResponse()
    {
        HttpRequestSendContext? captured = null;
        var runtimeNode = CreateNode(
            options => options.UseRequestSender((_) => new DelegateSender((context, _) =>
            {
                captured = context;
                return Task.FromResult(CreateResponse(context, 200, "ok"));
            })),
            new
            {
                baseUrl = "https://example.test/api/",
                defaultHeaders = new Dictionary<string, string>
                {
                    ["Accept"] = "application/json"
                }
            });
        var input = GetInput(runtimeNode);
        var output = LinkOutput<HttpResponseOutput>(
            runtimeNode,
            HttpComponentPorts.Output);

        await input.Target.SendAsync(new HttpRequestInput
        {
            Method = "post",
            Url = "items",
            Body = """{"name":"flux"}""",
            ContentType = "application/json",
            Headers = new Dictionary<string, string>
            {
                ["X-Request"] = "one"
            }
        });
        input.Target.Complete();
        var response = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        response.Success.ShouldBeTrue();
        response.StatusCode.ShouldBe(200);
        response.Body.ShouldBe("ok");
        captured.ShouldNotBeNull();
        captured.Method.ShouldBe("POST");
        captured.Url.ToString().ShouldBe("https://example.test/api/items");
        captured.Headers["Accept"].ShouldBe("application/json");
        captured.Headers["X-Request"].ShouldBe("one");
        captured.ContentType.ShouldBe("application/json");
        Encoding.UTF8.GetString(captured.BodyBytes!).ShouldBe("""{"name":"flux"}""");
    }

    [Fact]
    public async Task Request_UsesConfiguredClockForResponseTiming()
    {
        var startedAt = DateTimeOffset.Parse("2026-06-02T10:00:00Z");
        var completedAt = DateTimeOffset.Parse("2026-06-02T10:00:02.500Z");
        var clock = new RecordingHttpClock(startedAt, completedAt);
        IHttpClock? capturedClock = null;
        var runtimeNode = CreateNode(
            options => options
                .UseClock(clock)
                .UseRequestSender((context) =>
                {
                    capturedClock = context.Clock;
                    return new DelegateSender((sendContext, _) =>
                        Task.FromResult(CreateResponse(sendContext, 200, "ok") with
                        {
                            Timestamp = DateTimeOffset.UnixEpoch,
                            ElapsedMilliseconds = 42
                        }));
                }),
            new { });
        var input = GetInput(runtimeNode);
        var output = LinkOutput<HttpResponseOutput>(
            runtimeNode,
            HttpComponentPorts.Output);

        await input.Target.SendAsync(new HttpRequestInput { Url = "https://example.test/ok" });
        input.Target.Complete();
        var response = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        capturedClock.ShouldBeSameAs(clock);
        response.Timestamp.ShouldBe(completedAt);
        response.ElapsedMilliseconds.ShouldBe(2500);
    }

    [Fact]
    public async Task Request_UsesCaseInsensitiveContentTypeHeader()
    {
        HttpRequestSendContext? captured = null;
        var runtimeNode = CreateNode(
            options => options.UseRequestSender((_) => new DelegateSender((context, _) =>
            {
                captured = context;
                return Task.FromResult(CreateResponse(context, 200, "ok"));
            })),
            new { });
        var input = GetInput(runtimeNode);
        var output = LinkOutput<HttpResponseOutput>(
            runtimeNode,
            HttpComponentPorts.Output);

        await input.Target.SendAsync(new HttpRequestInput
        {
            Url = "https://example.test/items",
            Body = "{}",
            Headers = new Dictionary<string, string>
            {
                ["content-type"] = "application/json"
            }
        });
        input.Target.Complete();
        await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        captured.ShouldNotBeNull();
        captured.ContentType.ShouldBe("application/json");
    }

    [Fact]
    public async Task Request_EmitsNonSuccessResponseWithoutErrorByDefault()
    {
        var runtimeNode = CreateNode(
            options => options.UseRequestSender((_) => new DelegateSender((context, _) =>
                Task.FromResult(CreateResponse(context, 404, "missing")))),
            new { });
        var input = GetInput(runtimeNode);
        var output = LinkOutput<HttpResponseOutput>(
            runtimeNode,
            HttpComponentPorts.Output);
        var errors = LinkOutput<HttpErrorOutput>(
            runtimeNode,
            HttpComponentPorts.Errors);

        await input.Target.SendAsync(new HttpRequestInput { Url = "https://example.test/items/1" });
        input.Target.Complete();
        var response = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        response.StatusCode.ShouldBe(404);
        response.Success.ShouldBeFalse();
        errors.TryReceive(out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Request_EmitsErrorForNonSuccessWhenConfigured()
    {
        var runtimeNode = CreateNode(
            options => options.UseRequestSender((_) => new DelegateSender((context, _) =>
                Task.FromResult(CreateResponse(context, 500, "failed")))),
            new
            {
                treatNonSuccessStatusAsError = true
            });
        var input = GetInput(runtimeNode);
        var output = LinkOutput<HttpResponseOutput>(
            runtimeNode,
            HttpComponentPorts.Output);
        var errors = LinkOutput<HttpErrorOutput>(
            runtimeNode,
            HttpComponentPorts.Errors);

        await input.Target.SendAsync(new HttpRequestInput { Url = "https://example.test/items/1" });
        input.Target.Complete();
        var response = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        response.StatusCode.ShouldBe(500);
        response.Success.ShouldBeFalse();
        error.Kind.ShouldBe(HttpErrorKind.NonSuccessStatus);
        error.StatusCode.ShouldBe(500);
    }

    [Fact]
    public async Task Request_EmitsInvalidUrlErrorAndContinues()
    {
        var runtimeNode = CreateNode(
            options => options.UseRequestSender((_) => new DelegateSender((context, _) =>
                Task.FromResult(CreateResponse(context, 200, "ok")))),
            new { });
        var input = GetInput(runtimeNode);
        var output = LinkOutput<HttpResponseOutput>(
            runtimeNode,
            HttpComponentPorts.Output);
        var errors = LinkOutput<HttpErrorOutput>(
            runtimeNode,
            HttpComponentPorts.Errors);

        await input.Target.SendAsync(new HttpRequestInput { Url = "relative" });
        await input.Target.SendAsync(new HttpRequestInput { Url = "https://example.test/ok" });
        input.Target.Complete();
        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var response = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        error.Kind.ShouldBe(HttpErrorKind.InvalidUrl);
        response.Success.ShouldBeTrue();
        response.Body.ShouldBe("ok");
    }

    [Fact]
    public async Task Request_UsesConfiguredClockForRequestErrors()
    {
        var startedAt = DateTimeOffset.Parse("2026-06-02T11:00:00Z");
        var failedAt = DateTimeOffset.Parse("2026-06-02T11:00:01.250Z");
        var clock = new RecordingHttpClock(startedAt, failedAt);
        var runtimeNode = CreateNode(
            options => options
                .UseClock(clock)
                .UseRequestSender((_) => new DelegateSender((context, _) =>
                    Task.FromResult(CreateResponse(context, 200, "ok")))),
            new { });
        var input = GetInput(runtimeNode);
        var errors = LinkOutput<HttpErrorOutput>(
            runtimeNode,
            HttpComponentPorts.Errors);

        await input.Target.SendAsync(new HttpRequestInput { Url = "relative" });
        input.Target.Complete();
        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        error.Kind.ShouldBe(HttpErrorKind.InvalidUrl);
        error.Timestamp.ShouldBe(failedAt);
        error.ElapsedMilliseconds.ShouldBe(1250);
    }

    [Fact]
    public async Task Request_AllowsHostMatchingAllowedHosts()
    {
        var runtimeNode = CreateNode(
            options => options.UseRequestSender((_) => new DelegateSender((context, _) =>
                Task.FromResult(CreateResponse(context, 200, "ok")))),
            new
            {
                allowedHosts = new[] { "api.example.test", ".internal.example" }
            });
        var input = GetInput(runtimeNode);
        var output = LinkOutput<HttpResponseOutput>(
            runtimeNode,
            HttpComponentPorts.Output);

        await input.Target.SendAsync(new HttpRequestInput { Url = "https://api.example.test/items" });
        await input.Target.SendAsync(new HttpRequestInput { Url = "https://service.internal.example/items" });
        input.Target.Complete();
        var first = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var second = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        first.Success.ShouldBeTrue();
        second.Success.ShouldBeTrue();
    }

    [Fact]
    public async Task Request_RejectsHostOutsideAllowedHostsWithoutSending()
    {
        var sent = false;
        var runtimeNode = CreateNode(
            options => options.UseRequestSender((_) => new DelegateSender((context, _) =>
            {
                sent = true;
                return Task.FromResult(CreateResponse(context, 200, "ok"));
            })),
            new
            {
                allowedHosts = new[] { "api.example.test" }
            });
        var input = GetInput(runtimeNode);
        var errors = LinkOutput<HttpErrorOutput>(
            runtimeNode,
            HttpComponentPorts.Errors);

        await input.Target.SendAsync(new HttpRequestInput { Url = "https://evil.example.test/items" });
        input.Target.Complete();
        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        error.Kind.ShouldBe(HttpErrorKind.UrlNotAllowed);
        sent.ShouldBeFalse();
    }

    [Fact]
    public async Task Request_RestrictToBaseUrlOriginBlocksCrossOriginAbsoluteUrl()
    {
        HttpRequestSendContext? captured = null;
        var runtimeNode = CreateNode(
            options => options.UseRequestSender((_) => new DelegateSender((context, _) =>
            {
                captured = context;
                return Task.FromResult(CreateResponse(context, 200, "ok"));
            })),
            new
            {
                baseUrl = "https://example.test/api/",
                restrictToBaseUrlOrigin = true
            });
        var input = GetInput(runtimeNode);
        var output = LinkOutput<HttpResponseOutput>(
            runtimeNode,
            HttpComponentPorts.Output);
        var errors = LinkOutput<HttpErrorOutput>(
            runtimeNode,
            HttpComponentPorts.Errors);

        await input.Target.SendAsync(new HttpRequestInput { Url = "https://attacker.test/steal" });
        await input.Target.SendAsync(new HttpRequestInput { Url = "items" });
        input.Target.Complete();
        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var response = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        error.Kind.ShouldBe(HttpErrorKind.UrlNotAllowed);
        response.Success.ShouldBeTrue();
        captured.ShouldNotBeNull();
        captured.Url.ToString().ShouldBe("https://example.test/api/items");
    }

    [Fact]
    public async Task Request_RejectsHeadersWithForbiddenCharacters()
    {
        var sent = false;
        var runtimeNode = CreateNode(
            options => options.UseRequestSender((_) => new DelegateSender((context, _) =>
            {
                sent = true;
                return Task.FromResult(CreateResponse(context, 200, "ok"));
            })),
            new { });
        var input = GetInput(runtimeNode);
        var errors = LinkOutput<HttpErrorOutput>(
            runtimeNode,
            HttpComponentPorts.Errors);

        await input.Target.SendAsync(new HttpRequestInput
        {
            Url = "https://example.test/items",
            Headers = new Dictionary<string, string>
            {
                ["X-Injected"] = "value\r\nHost: attacker.test"
            }
        });
        input.Target.Complete();
        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        error.Kind.ShouldBe(HttpErrorKind.InvalidRequest);
        sent.ShouldBeFalse();
    }

    [Fact]
    public async Task Request_EmitsTimeoutError()
    {
        var runtimeNode = CreateNode(
            options => options.UseRequestSender((_) => new DelegateSender(async (_, cancellationToken) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new UnreachableException();
            })),
            new { });
        var input = GetInput(runtimeNode);
        var errors = LinkOutput<HttpErrorOutput>(
            runtimeNode,
            HttpComponentPorts.Errors);

        await input.Target.SendAsync(new HttpRequestInput
        {
            Url = "https://example.test/slow",
            TimeoutMilliseconds = 1
        });
        input.Target.Complete();
        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        error.Kind.ShouldBe(HttpErrorKind.Timeout);
    }

    [Fact]
    public async Task Request_EmitsCanceledError()
    {
        var runtimeNode = CreateNode(
            options => options.UseRequestSender((_) => new DelegateSender((_, _) =>
                throw new OperationCanceledException("stopped"))),
            new { });
        var input = GetInput(runtimeNode);
        var errors = LinkOutput<HttpErrorOutput>(
            runtimeNode,
            HttpComponentPorts.Errors);

        await input.Target.SendAsync(new HttpRequestInput
        {
            Url = "https://example.test/canceled"
        });
        input.Target.Complete();
        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        error.Kind.ShouldBe(HttpErrorKind.Canceled);
    }

    [Fact]
    public async Task Request_DefaultSenderReadsResponse()
    {
        await using var server = await TestHttpServer.StartAsync(
            "application/json; charset=utf-8",
            """{"ok":true}""");
        var runtimeNode = CreateNode(_ => { }, new { });
        var input = GetInput(runtimeNode);
        var output = LinkOutput<HttpResponseOutput>(
            runtimeNode,
            HttpComponentPorts.Output);

        await input.Target.SendAsync(new HttpRequestInput { Url = server.Url });
        input.Target.Complete();
        var response = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        response.Success.ShouldBeTrue();
        response.StatusCode.ShouldBe(200);
        response.ContentType.ShouldBe("application/json; charset=utf-8");
        response.Body.ShouldBe("""{"ok":true}""");
        response.BodyBytes.ShouldBe(Encoding.UTF8.GetBytes("""{"ok":true}"""));
    }

    [Fact]
    public async Task Request_DoesNotFollowRedirectToDisallowedHostUnderAllowList()
    {
        await using var server = await RedirectingTestServer.StartAsync(
            "https://attacker.test/steal");
        var runtimeNode = CreateNode(_ => { }, new
        {
            allowedHosts = new[] { "127.0.0.1" }
        });
        var input = GetInput(runtimeNode);
        var output = LinkOutput<HttpResponseOutput>(
            runtimeNode,
            HttpComponentPorts.Output);

        await input.Target.SendAsync(new HttpRequestInput { Url = server.Url });
        input.Target.Complete();
        var response = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        response.StatusCode.ShouldBe(302);
        response.Headers["Location"].ShouldBe(["https://attacker.test/steal"]);
        server.RequestCount.ShouldBe(1);
    }

    [Fact]
    public async Task Request_FollowsRedirectWhenNoAllowListGuardConfigured()
    {
        await using var target = await TestHttpServer.StartAsync(
            "text/plain",
            "redirected");
        await using var server = await RedirectingTestServer.StartAsync(target.Url);
        var runtimeNode = CreateNode(_ => { }, new { });
        var input = GetInput(runtimeNode);
        var output = LinkOutput<HttpResponseOutput>(
            runtimeNode,
            HttpComponentPorts.Output);

        await input.Target.SendAsync(new HttpRequestInput { Url = server.Url });
        input.Target.Complete();
        var response = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        response.StatusCode.ShouldBe(200);
        response.Body.ShouldBe("redirected");
        server.RequestCount.ShouldBe(1);
    }

    [Fact]
    public async Task Request_EnforcesMaxResponseBodySize()
    {
        await using var server = await TestHttpServer.StartAsync(
            "text/plain",
            "too-large");
        var runtimeNode = CreateNode(_ => { }, new
        {
            maxResponseBodyBytes = 3
        });
        var input = GetInput(runtimeNode);
        var errors = LinkOutput<HttpErrorOutput>(
            runtimeNode,
            HttpComponentPorts.Errors);

        await input.Target.SendAsync(new HttpRequestInput { Url = server.Url });
        input.Target.Complete();
        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        error.Kind.ShouldBe(HttpErrorKind.ResponseTooLarge);
    }

    [Fact]
    public async Task Request_EmitsDiagnostics()
    {
        var runtimeNode = CreateNode(
            options => options.UseRequestSender((_) => new DelegateSender((context, _) =>
                Task.FromResult(CreateResponse(context, 200, "ok")))),
            new { });
        var input = GetInput(runtimeNode);
        var output = LinkOutput<HttpResponseOutput>(
            runtimeNode,
            HttpComponentPorts.Output);
        var diagnostics = new BufferBlock<FlowDiagnostic>();
        runtimeNode.Node.ShouldBeAssignableTo<IFlowDiagnosticSource>()!
            .Diagnostics.LinkTo(
                diagnostics,
                new DataflowLinkOptions { PropagateCompletion = true });

        await input.Target.SendAsync(new HttpRequestInput { Url = "https://example.test/ok" });
        input.Target.Complete();
        await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var diagnostic = await diagnostics.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        diagnostic.Name.ShouldBe(HttpDiagnosticNames.RequestSucceeded);
        diagnostic.Attributes["statusCode"].ShouldBe(200);
        diagnostic.Attributes["success"].ShouldBe(true);
    }

    [Fact]
    public void Request_RejectsInvalidOptions()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => CreateNode(_ => { }, new { boundedCapacity = 0 }));

        exception.Message.ShouldContain("boundedCapacity");
    }

    [Fact]
    public void RegisterHttpComponents_RegistersRequestNode()
    {
        var registry = new RuntimeNodeFactoryRegistry()
            .RegisterHttpComponents();

        registry.TryGetFactory(HttpComponentTypes.Request, out _).ShouldBeTrue();
    }

    private static RuntimeNode CreateNode(
        Action<HttpComponentOptions> configure,
        object configuration)
    {
        var registry = new RuntimeNodeFactoryRegistry()
            .RegisterHttpComponents(configure);
        registry.TryGetFactory(HttpComponentTypes.Request, out var factory).ShouldBeTrue();
        return factory(CreateContext(configuration));
    }

    private static RuntimeNodeFactoryContext CreateContext(object configuration)
    {
        var root = JsonSerializer.SerializeToElement(configuration);
        var values = root.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.Clone());

        return new RuntimeNodeFactoryContext(
            new NodeName("request"),
            new NodeDefinition
            {
                Type = HttpComponentTypes.Request,
                Configuration = values
            },
            "main",
            new Dictionary<NodeName, RuntimeNode>());
    }

    private static InputPort<HttpRequestInput> GetInput(RuntimeNode runtimeNode)
        => runtimeNode.FindInput(new PortName(HttpComponentPorts.Input))
            .ShouldBeOfType<InputPort<HttpRequestInput>>();

    private static BufferBlock<T> LinkOutput<T>(
        RuntimeNode runtimeNode,
        string portName)
    {
        var target = new BufferBlock<T>();
        runtimeNode.FindOutput(new PortName(portName))!
            .TryLinkTo(
                new InputPort<T>(
                    new PortAddress("test", new NodeName("items"), new PortName("Input")),
                    target),
                propagateCompletion: true,
                out var error);
        error.ShouldBeNull();
        return target;
    }

    private static HttpResponseOutput CreateResponse(
        HttpRequestSendContext context,
        int statusCode,
        string body)
        => new()
        {
            Method = context.Method,
            Url = context.Url.ToString(),
            StatusCode = statusCode,
            ReasonPhrase = statusCode is >= 200 and <= 299 ? "OK" : "Error",
            Headers = new Dictionary<string, string[]>
            {
                ["Content-Type"] = ["text/plain"]
            },
            BodyBytes = Encoding.UTF8.GetBytes(body),
            Body = body,
            ContentType = "text/plain",
            ElapsedMilliseconds = 1,
            Success = statusCode is >= 200 and <= 299
        };

    private sealed class DelegateSender(
        Func<HttpRequestSendContext, CancellationToken, Task<HttpResponseOutput>> send)
        : IHttpRequestSender
    {
        public Task<HttpResponseOutput> SendAsync(
            HttpRequestSendContext context,
            CancellationToken cancellationToken = default)
            => send(context, cancellationToken);

        public ValueTask DisposeAsync()
            => ValueTask.CompletedTask;
    }

    private sealed class TestHttpServer : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly Task _serverTask;

        private TestHttpServer(
            TcpListener listener,
            Task serverTask,
            string url)
        {
            _listener = listener;
            _serverTask = serverTask;
            Url = url;
        }

        public string Url { get; }

        public static Task<TestHttpServer> StartAsync(
            string contentType,
            string body)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var endpoint = (IPEndPoint)listener.LocalEndpoint;
            var server = new TestHttpServer(
                listener,
                HandleAsync(listener, contentType, body),
                $"http://127.0.0.1:{endpoint.Port}/");
            return Task.FromResult(server);
        }

        public async ValueTask DisposeAsync()
        {
            _listener.Stop();
            try
            {
                await _serverTask.ConfigureAwait(false);
            }
            catch (SocketException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private static async Task HandleAsync(
            TcpListener listener,
            string contentType,
            string body)
        {
            using var client = await listener.AcceptTcpClientAsync()
                .ConfigureAwait(false);
            await using var stream = client.GetStream();
            await ReadHeadersAsync(stream).ConfigureAwait(false);

            var bodyBytes = Encoding.UTF8.GetBytes(body);
            var header =
                "HTTP/1.1 200 OK\r\n" +
                $"Content-Type: {contentType}\r\n" +
                $"Content-Length: {bodyBytes.Length}\r\n" +
                "Connection: close\r\n" +
                "\r\n";
            var headerBytes = Encoding.ASCII.GetBytes(header);
            await stream.WriteAsync(headerBytes).ConfigureAwait(false);
            await stream.WriteAsync(bodyBytes).ConfigureAwait(false);
        }

        private static async Task ReadHeadersAsync(NetworkStream stream)
        {
            var buffer = new byte[1];
            var previous = new Queue<byte>(4);
            while (await stream.ReadAsync(buffer).ConfigureAwait(false) == 1)
            {
                previous.Enqueue(buffer[0]);
                if (previous.Count > 4)
                {
                    previous.Dequeue();
                }

                if (previous.Count == 4 &&
                    previous.SequenceEqual("\r\n\r\n"u8.ToArray()))
                {
                    return;
                }
            }
        }
    }

    private sealed class RedirectingTestServer : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly string _location;
        private readonly Task _serverTask;
        private int _requestCount;

        private RedirectingTestServer(
            TcpListener listener,
            string url,
            string location)
        {
            _listener = listener;
            Url = url;
            _location = location;
            _serverTask = AcceptAsync();
        }

        public string Url { get; }

        public int RequestCount => Volatile.Read(ref _requestCount);

        public static Task<RedirectingTestServer> StartAsync(string location)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var endpoint = (IPEndPoint)listener.LocalEndpoint;
            var server = new RedirectingTestServer(
                listener,
                $"http://127.0.0.1:{endpoint.Port}/",
                location);
            return Task.FromResult(server);
        }

        public async ValueTask DisposeAsync()
        {
            _listener.Stop();
            try
            {
                await _serverTask.ConfigureAwait(false);
            }
            catch (SocketException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private async Task AcceptAsync()
        {
            while (true)
            {
                using var client = await _listener.AcceptTcpClientAsync()
                    .ConfigureAwait(false);
                Interlocked.Increment(ref _requestCount);
                await using var stream = client.GetStream();
                await ReadHeadersAsync(stream).ConfigureAwait(false);

                var header =
                    "HTTP/1.1 302 Found\r\n" +
                    $"Location: {_location}\r\n" +
                    "Content-Length: 0\r\n" +
                    "Connection: close\r\n" +
                    "\r\n";
                var headerBytes = Encoding.ASCII.GetBytes(header);
                await stream.WriteAsync(headerBytes).ConfigureAwait(false);
            }
        }

        private static async Task ReadHeadersAsync(NetworkStream stream)
        {
            var buffer = new byte[1];
            var previous = new Queue<byte>(4);
            while (await stream.ReadAsync(buffer).ConfigureAwait(false) == 1)
            {
                previous.Enqueue(buffer[0]);
                if (previous.Count > 4)
                {
                    previous.Dequeue();
                }

                if (previous.Count == 4 &&
                    previous.SequenceEqual("\r\n\r\n"u8.ToArray()))
                {
                    return;
                }
            }
        }
    }
}
