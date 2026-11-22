using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Application;
using FluxFlow.Configuration.Application;
using FluxFlow.Configuration.Components.Http;
using FluxFlow.Configuration.Components.Sources;
using FluxFlow.Core.Messaging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;
using NativeClient = System.Net.Http.HttpClient;

namespace FluxFlow.Components.Http.Tests;

public sealed partial class HttpTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);
    private static FlowMessage Message(object value) => FlowMessage.Create(new JsonFlowValue(JsonSerializer.SerializeToElement(value)));
    private static BufferBlock<FlowMessage> Capture(ISourceBlock<FlowMessage> source)
    {
        var target = new BufferBlock<FlowMessage>();
        source.LinkTo(target);
        return target;
    }

    [Theory]
    [InlineData(200, false)]
    [InlineData(201, true)]
    [InlineData(404, false)]
    [InlineData(503, true)]
    public async Task Requests_preserve_status_body_headers_and_lineage(int status, bool jsonInput)
    {
        var observed = new TaskCompletionSource<string[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = await Server.Start(async context =>
        {
            using var reader = new StreamReader(context.Request.Body);
            observed.SetResult([context.Request.Method, context.Request.Path + context.Request.QueryString,
                context.Request.Headers.Authorization.ToString(), context.Request.Headers["X-Default"].ToString(),
                context.Request.ContentType!, await reader.ReadToEndAsync()]);
            context.Response.StatusCode = status;
            context.Response.Headers.Append("X-Result", new Microsoft.Extensions.Primitives.StringValues(["one", "two"]));
            context.Response.ContentType = "application/json; charset=utf-8";
            await context.Response.WriteAsync("{\"accepted\":true}");
        });
        using var client = new NativeClient { BaseAddress = new Uri(server.Address, "api/") };
        client.DefaultRequestHeaders.Add("Authorization", "Bearer default-token");
        client.DefaultRequestHeaders.Add("X-Default", "present");
        await using var component = new HttpClientComponent(new() { Client = client });
        var output = Capture(component.Output);
        var request = new HttpSendRequest
        {
            Method = "POST", RequestUrl = "items?limit=2", Body = "{\"value\":42}", ContentType = "application/json",
            Headers = new Dictionary<string, string> { ["Authorization"] = "Bearer request-token" }
        };
        FlowValue value = jsonInput ? new JsonFlowValue(request.Json) : request;
        var message = FlowMessage.Create(value, correlationId: CorrelationId.New(),
            headers: new Dictionary<string, FlowValue> { ["custom"] = new JsonFlowValue(JsonSerializer.SerializeToElement("kept")) });
        await component.Input.SendAsync(message);
        var result = await output.ReceiveAsync(Deadline);
        result.IsError.ShouldBeFalse(result.Error?.Message);
        var response = result.Value.ShouldBeOfType<HttpResponse>();
        response.StatusCode.ShouldBe(status);
        response.Body.ShouldBe("{\"accepted\":true}");
        response.Headers["X-Result"].ShouldBe(["one", "two"]);
        response.Headers["content-type"].Single().ShouldBe("application/json; charset=utf-8");
        response.Json.GetProperty("StatusCode").GetInt32().ShouldBe(status);
        result.TraceId.ShouldBe(message.TraceId);
        result.CorrelationId.ShouldBe(message.CorrelationId);
        result.CausationId.ShouldBe(message.MessageId);
        result.Headers["custom"].Json.GetString().ShouldBe("kept");
        (await observed.Task.WaitAsync(Deadline)).ShouldBe(["POST", "/api/items?limit=2", "Bearer request-token", "present", "application/json", "{\"value\":42}"]);
    }

    [Fact]
    public async Task Binary_bodies_round_trip_without_conversion()
    {
        byte[] bytes = [0, 255, 128, 10, 20];
        await using var server = await Server.Start(async context =>
        {
            context.Response.ContentType = "application/octet-stream";
            await context.Request.Body.CopyToAsync(context.Response.Body);
        });
        using var client = new NativeClient();
        await using var component = new HttpClientComponent(new() { Client = client });
        var output = Capture(component.Output);
        await component.Input.SendAsync(Message(new
        {
            method = "PUT", requestUrl = server.Address.ToString(), body = Convert.ToBase64String(bytes),
            contentType = "application/octet-stream", encoding = "Base64", responseEncoding = "Base64"
        }));
        var response = (await output.ReceiveAsync(Deadline)).Value.ShouldBeOfType<HttpResponse>();
        response.Encoding.ShouldBe(HttpBodyEncoding.Base64);
        Convert.FromBase64String(response.Body).ShouldBe(bytes);
    }

    [Theory]
    [InlineData("HEAD", 200)]
    [InlineData("DELETE", 204)]
    public async Task Empty_responses_are_valid(string method, int status)
    {
        await using var server = await Server.Start(context => { context.Response.StatusCode = status; return Task.CompletedTask; });
        using var client = new NativeClient { BaseAddress = server.Address };
        await using var component = new HttpClientComponent(new() { Client = client });
        var output = Capture(component.Output);
        await component.Input.SendAsync(Message(new { method, requestUrl = "/" }));
        var response = (await output.ReceiveAsync(Deadline)).Value.ShouldBeOfType<HttpResponse>();
        response.StatusCode.ShouldBe(status);
        response.Body.ShouldBeEmpty();
    }

    [Fact]
    public async Task Native_charset_decoding_is_used_for_text()
    {
        await using var server = await Server.Start(async context =>
        {
            context.Response.ContentType = "text/plain; charset=iso-8859-1";
            await context.Response.Body.WriteAsync(Encoding.Latin1.GetBytes("café"));
        });
        using var client = new NativeClient { BaseAddress = server.Address };
        await using var component = new HttpClientComponent(new() { Client = client });
        var output = Capture(component.Output);
        await component.Input.SendAsync(Message(new { requestUrl = "/" }));
        (await output.ReceiveAsync(Deadline)).Value.ShouldBeOfType<HttpResponse>().Body.ShouldBe("café");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Explicit_request_charset_controls_bytes_sent(bool useHeader)
    {
        var bytes = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = await Server.Start(async context =>
        {
            using var body = new MemoryStream();
            await context.Request.Body.CopyToAsync(body);
            bytes.SetResult(body.ToArray());
        });
        using var client = new NativeClient { BaseAddress = server.Address };
        await using var component = new HttpClientComponent(new() { Client = client });
        var output = Capture(component.Output);
        const string contentType = "text/plain; charset=iso-8859-1";
        await component.Input.SendAsync(Message(new
        {
            requestUrl = "/", method = "POST", body = "café", contentType = useHeader ? null : contentType,
            headers = useHeader ? new Dictionary<string, string> { ["content-type"] = contentType } : new()
        }));
        (await output.ReceiveAsync(Deadline)).IsError.ShouldBeFalse();
        (await bytes.Task.WaitAsync(Deadline)).ShouldBe(Encoding.Latin1.GetBytes("café"));
    }

    [Fact]
    public async Task Per_request_headers_are_isolated_and_native_messages_are_disposed()
    {
        HttpRequestMessage? lastRequest = null;
        var content = new List<TrackedContent>();
        var authorization = new List<string?>();
        using var handler = new Handler((request, _) =>
        {
            lastRequest = request;
            authorization.Add(request.Headers.Authorization?.ToString());
            var body = new TrackedContent();
            content.Add(body);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = body });
        });
        using var client = new NativeClient(handler);
        client.DefaultRequestHeaders.Add("Authorization", "Bearer default");
        await using var component = new HttpClientComponent(new() { Client = client });
        var output = Capture(component.Output);
        var headers = new Dictionary<string, string> { ["Authorization"] = "Bearer override" };
        var first = new HttpSendRequest { RequestUrl = "https://example.test/", Method = "POST", Body = "first", Headers = headers };
        headers["Authorization"] = "Bearer mutated";
        foreach (var request in new[] { first, new HttpSendRequest { RequestUrl = "https://example.test/", Method = "POST", Body = "second" } })
        {
            await component.Input.SendAsync(FlowMessage.Create(request));
            (await output.ReceiveAsync(Deadline)).Value.ShouldBeOfType<HttpResponse>().Body.ShouldBe("response");
            content.Last().Disposed.ShouldBeTrue();
            await Should.ThrowAsync<ObjectDisposedException>(() => lastRequest!.Content!.ReadAsStringAsync());
        }
        authorization.ShouldBe(["Bearer override", "Bearer default"]);
        client.DefaultRequestHeaders.Authorization!.ToString().ShouldBe("Bearer default");
    }

    [Theory]
    [InlineData("{\"requestUrl\":\"ftp://localhost/file\"}")]
    [InlineData("{\"requestUrl\":\"http://user:pass@localhost/\"}")]
    [InlineData("{\"requestUrl\":\"/relative\"}")]
    [InlineData("{\"requestUrl\":\"http://localhost/\",\"method\":\"bad method\"}")]
    [InlineData("{\"requestUrl\":\"http://localhost/\",\"body\":\"!\",\"encoding\":\"Base64\"}")]
    [InlineData("{\"requestUrl\":\"http://localhost/\",\"encoding\":99}")]
    [InlineData("{\"requestUrl\":\"http://localhost/\",\"responseEncoding\":\"Json\"}")]
    [InlineData("{\"requestUrl\":\"http://localhost/\",\"timeout\":\"00:00:00\"}")]
    [InlineData("{\"requestUrl\":\"http://localhost/\",\"body\":{},\"encoding\":\"Base64\"}")]
    [InlineData("{\"requestUrl\":\"http://localhost/\",\"contentType\":\"text/plain\"}")]
    [InlineData("{\"requestUrl\":\"http://localhost/\",\"body\":\"text\",\"contentType\":\"text/plain\",\"headers\":{\"Content-Type\":\"text/plain\"}}")]
    [InlineData("{\"requestUrl\":\"http://localhost/\",\"headers\":{\"X-Test\":\"ok\\r\\nInjected: bad\"}}")]
    [InlineData("{}")]
    public async Task Malformed_requests_become_processing_errors_without_network_work(string json)
    {
        using var handler = new Handler((_, _) => throw new Exception("Should not send."));
        using var client = new NativeClient(handler);
        await using var component = new HttpClientComponent(new() { Client = client });
        var output = Capture(component.Output);
        await component.Input.SendAsync(FlowMessage.Create(new JsonFlowValue(JsonDocument.Parse(json).RootElement.Clone())));
        (await output.ReceiveAsync(Deadline)).IsError.ShouldBeTrue();
        handler.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task Transport_failure_is_an_error_and_next_input_can_succeed_without_retry()
    {
        var count = 0;
        using var handler = new Handler((_, _) => ++count == 1
            ? throw new HttpRequestException("Unavailable") : Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        using var client = new NativeClient(handler);
        await using var component = new HttpClientComponent(new() { Client = client });
        var output = Capture(component.Output);
        var upstream = FlowMessage.Create(new FlowError("upstream", "failure", "test"));
        await component.Input.SendAsync(upstream);
        (await output.ReceiveAsync(Deadline)).ShouldBeSameAs(upstream);
        handler.Calls.ShouldBe(0);
        await component.Input.SendAsync(Message(new { requestUrl = "https://example.test/" }));
        (await output.ReceiveAsync(Deadline)).IsError.ShouldBeTrue();
        handler.Calls.ShouldBe(1);
        await component.Input.SendAsync(Message(new { requestUrl = "https://example.test/" }));
        (await output.ReceiveAsync(Deadline)).Value.ShouldBeOfType<HttpResponse>().StatusCode.ShouldBe(200);
        handler.Calls.ShouldBe(2);
    }

    [Fact]
    public async Task Timeout_covers_body_reading_and_does_not_fault_component()
    {
        await using var server = await Server.Start(async context =>
        {
            await context.Response.WriteAsync("partial");
            await context.Response.Body.FlushAsync();
            try { await Task.Delay(Deadline, context.RequestAborted); } catch (OperationCanceledException) { }
        });
        using var client = new NativeClient { BaseAddress = server.Address, Timeout = TimeSpan.FromMilliseconds(200) };
        await using var component = new HttpClientComponent(new() { Client = client });
        var output = Capture(component.Output);
        await component.Input.SendAsync(Message(new { requestUrl = "/" }));
        (await output.ReceiveAsync(Deadline)).IsError.ShouldBeTrue();
        component.Completion.IsCompleted.ShouldBeFalse();
    }

    [Fact]
    public async Task Response_size_is_bounded_even_without_content_length()
    {
        await using var server = await Server.Start(async context =>
        {
            for (var i = 0; i < 4; i++)
            {
                await context.Response.WriteAsync(new string('a', 1024));
                await context.Response.Body.FlushAsync();
            }
        });
        using var client = new NativeClient { BaseAddress = server.Address, MaxResponseContentBufferSize = 1024 };
        await using var component = new HttpClientComponent(new() { Client = client });
        var output = Capture(component.Output);
        await component.Input.SendAsync(Message(new { requestUrl = "/" }));
        (await output.ReceiveAsync(Deadline)).IsError.ShouldBeTrue();
    }

    [Fact]
    public async Task Fault_cancels_only_this_component_and_does_not_dispose_shared_client()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new Handler(async (_, token) =>
        {
            started.TrySetResult();
            try { await Task.Delay(Deadline, token); }
            catch (OperationCanceledException) { canceled.TrySetResult(); throw; }
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var client = new NativeClient(handler);
        await using var component = new HttpClientComponent(new() { Client = client });
        await component.Input.SendAsync(Message(new { requestUrl = "https://example.test/" }));
        await started.Task.WaitAsync(Deadline);
        component.Fault(new InvalidOperationException("stop"));
        await canceled.Task.WaitAsync(Deadline);
        await Should.ThrowAsync<Exception>(() => component.Completion.WaitAsync(Deadline));
        await component.DisposeAsync();
        handler.Disposed.ShouldBeFalse();
        client.Dispose();
        handler.Disposed.ShouldBeTrue();
    }

    [Fact]
    public async Task Configuration_defers_network_and_links_source_to_shared_native_client()
    {
        var calls = 0;
        await using var server = await Server.Start(context =>
        {
            Interlocked.Increment(ref calls);
            context.Request.Headers.Authorization.ToString().ShouldBe("Bearer configured");
            context.Response.StatusCode = 302;
            context.Response.Headers.Location = "/elsewhere";
            return Task.CompletedTask;
        });
        var json = $$"""
        { "Resources": { "api": { "Type": "http.client", "BaseAddress": "{{server.Address}}", "Headers": { "Authorization": "Bearer configured" } } },
          "Workflows": { "demo": {
            "samples": { "Type": "source", "Values": "[{\"requestUrl\":\"/\"}]", "Output": "send.Input" },
            "send": { "Type": "http.client", "Client": "api", "Input": "samples.Output" },
            "other": { "Type": "http.client", "Client": "api" }
          } } }
        """;
        using var config = (ConfigurationRoot)new ConfigurationBuilder().AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(json))).Build();
        var services = new ServiceCollection().AddHttp().AddSource();
        services.AddFluxFlowApplication(config);
        Volatile.Read(ref calls).ShouldBe(0);
        await using var provider = services.BuildServiceProvider();
        await using var app = await provider.BuildApplicationAsync();
        var client = provider.GetRequiredFlowResource<NativeClient>("api");
        client.ShouldBeSameAs(provider.GetRequiredFlowResource<NativeClient>("api"));
        Volatile.Read(ref calls).ShouldBe(0);
        var output = Capture(provider.GetWorkflowServices("demo").GetRequiredKeyedService<ISourceBlock<FlowMessage>>("demo.send.Output"));
        _ = provider.GetWorkflowServices("demo").GetRequiredKeyedService<ISourceBlock<FlowMessage>>("demo.send.Events");
        await app.StartAsync();
        await provider.GetWorkflowServices("demo").GetRequiredKeyedService<ITargetBlock<FlowMessage>>("demo.samples.Input").SendAsync(Message(new { }));
        (await output.ReceiveAsync(Deadline)).Value.ShouldBeOfType<HttpResponse>().StatusCode.ShouldBe(302);
        Volatile.Read(ref calls).ShouldBe(1); // No redirect or duplicate input/output-side connection.
        await app.StopAsync().WaitAsync(Deadline);
        using var response = await client.GetAsync("/"); // Application does not own resource disposal.
        response.StatusCode.ShouldBe(HttpStatusCode.Found);
        await provider.DisposeAsync();
        await Should.ThrowAsync<ObjectDisposedException>(() => client.GetAsync("/"));
    }

    [Theory]
    [InlineData("BaseAddress", "\"relative\"")]
    [InlineData("BaseAddress", "\"ftp://localhost/\"")]
    [InlineData("BaseAddress", "\"https://user:password@localhost/\"")]
    [InlineData("Timeout", "\"00:00:00\"")]
    [InlineData("MaxResponseContentBufferSize", "0")]
    [InlineData("PooledConnectionLifetime", "\"-00:00:01\"")]
    [InlineData("Headers", "{\"Invalid Header\":\"value\"}")]
    [InlineData("Authentication", "{\"Scheme\":\"unknown\"}")]
    [InlineData("Authentication", "{\"Scheme\":\"Basic\",\"Username\":\"user\"}")]
    [InlineData("Authentication", "{\"Scheme\":\"Basic\",\"Username\":\"user:name\",\"Password\":\"pass\"}")]
    [InlineData("Authentication", "{\"Scheme\":\"Bearer\",\"Token\":\"ok\\r\\nInjected: value\"}")]
    [InlineData("Authentication", "{\"Scheme\":\"Bearer\",\"Token\":\"test\",\"ClientId\":\"wrong\"}")]
    [InlineData("Authentication", "{\"Scheme\":\"OAuthClientCredentials\",\"TokenEndpoint\":\"http://example.test/token\",\"ClientId\":\"test\",\"ClientSecret\":\"secret\"}")]
    [InlineData("Authentication", "{\"Scheme\":\"OAuthClientCredentials\",\"TokenEndpoint\":\"https://example.test/token\",\"ClientId\":\"test\"}")]
    [InlineData("Authentication", "{\"Scheme\":\"OAuthClientCredentials\",\"TokenEndpoint\":\"https://example.test/token\",\"ClientId\":\"test\",\"ClientSecret\":\"secret\",\"Token\":\"conflict\"}")]
    [InlineData("Retry", "{\"MaxRetryAttempts\":-1}")]
    [InlineData("Retry", "{\"Delay\":\"-00:00:01\"}")]
    public void Invalid_resource_configuration_is_rejected_atomically(string property, string value)
    {
        var json = $$"""
        { "Resources": { "api": { "Type": "http.client", "{{property}}": {{value}} } },
          "Workflows": { "demo": { "send": { "Type": "http.client", "Client": "api" } } } }
        """;
        using var config = (ConfigurationRoot)new ConfigurationBuilder().AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(json))).Build();
        var services = new ServiceCollection().AddHttp();
        var before = services.ToArray();
        Should.Throw<Exception>(() => services.AddFluxFlowApplication(config)).ToString().ShouldContain("api");
        services.ToArray().ShouldBe(before);
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public int Calls;
        public bool Disposed;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return send(request, cancellationToken);
        }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }

    private sealed class TrackedContent() : ByteArrayContent(Encoding.UTF8.GetBytes("response"))
    {
        public bool Disposed;
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }

    private sealed class Server(WebApplication app, Uri address) : IAsyncDisposable
    {
        public Uri Address => address;
        public static async Task<Server> Start(RequestDelegate handler)
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.UseSetting("urls", "http://127.0.0.1:0");
            var app = builder.Build();
            app.Run(handler);
            await app.StartAsync();
            return new Server(app, new Uri(app.Urls.Single()));
        }
        public async ValueTask DisposeAsync() { await app.StopAsync(); await app.DisposeAsync(); }
    }
}
