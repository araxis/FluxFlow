using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Application;
using FluxFlow.Configuration.Application;
using FluxFlow.Configuration.Components.Http;
using FluxFlow.Core.Messaging;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;
using NativeClient = System.Net.Http.HttpClient;

namespace FluxFlow.Components.Http.Tests;

public sealed partial class HttpTests
{
    private static ServiceProvider ResourceProvider(object resource, TimeProvider? time = null)
    {
        var json = JsonSerializer.Serialize(new { Resources = new { api = resource },
            Workflows = new { demo = new { send = new { Type = "http.client", Client = "api" } } } });
        using var config = (ConfigurationRoot)new ConfigurationBuilder().AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(json))).Build();
        var services = new ServiceCollection().AddHttp();
        if (time is not null) services.AddSingleton(time);
        services.AddFluxFlowApplication(config);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    [Theory]
    [InlineData("Basic", "Basic dXNlcjpwYXNz")]
    [InlineData("Bearer", "Bearer static-token")]
    public async Task Resource_authentication_and_request_override_are_isolated(string scheme, string expected)
    {
        var headers = new List<string>();
        await using var server = await Server.Start(context =>
        {
            headers.Add(context.Request.Headers.Authorization.ToString());
            return Task.CompletedTask;
        });
        object auth = scheme == "Basic" ? new { Scheme = scheme, Username = "user", Password = "pass" }
            : new { Scheme = scheme, Token = "static-token" };
        await using var provider = ResourceProvider(new { Type = "http.client", BaseAddress = server.Address, Authentication = auth });
        var client = provider.GetRequiredFlowResource<NativeClient>("api");
        await using var component = new HttpClientComponent(new() { Client = client });
        var output = Capture(component.Output);
        foreach (var request in new[] {
            new HttpSendRequest { RequestUrl = "/" },
            new HttpSendRequest { RequestUrl = "/", Headers = new Dictionary<string, string> { ["Authorization"] = "Bearer override" } },
            new HttpSendRequest { RequestUrl = "/" } })
        {
            await component.Input.SendAsync(FlowMessage.Create(request));
            (await output.ReceiveAsync(Deadline)).IsError.ShouldBeFalse();
        }
        headers.ShouldBe([expected, "Bearer override", expected]);
    }

    [Theory]
    [InlineData("GET", false, 503, 2, 3)]
    [InlineData("GET", false, 429, 2, 3)]
    [InlineData("GET", false, 400, 2, 1)]
    [InlineData("GET", false, 503, 0, 1)]
    [InlineData("POST", false, 503, 2, 1)]
    [InlineData("POST", true, 503, 2, 3)]
    public async Task Resource_retry_respects_budget_method_and_status(string method, bool unsafeMethods, int status, int retries, int expected)
    {
        var bodies = new List<string>();
        await using var server = await Server.Start(async context =>
        {
            using var reader = new StreamReader(context.Request.Body);
            bodies.Add(await reader.ReadToEndAsync());
            context.Response.StatusCode = status;
        });
        await using var provider = ResourceProvider(new
        {
            Type = "http.client", BaseAddress = server.Address,
            Retry = new { MaxRetryAttempts = retries, Delay = "00:00:00.001", UseJitter = false, RetryUnsafeMethods = unsafeMethods }
        });
        await using var component = new HttpClientComponent(new() { Client = provider.GetRequiredFlowResource<NativeClient>("api") });
        var output = Capture(component.Output);
        await component.Input.SendAsync(Message(new { method, requestUrl = "/", body = new { amount = 123 } }));
        var result = await output.ReceiveAsync(Deadline);
        result.IsError.ShouldBeFalse(result.Error?.Message);
        result.Value.ShouldBeOfType<HttpResponse>().StatusCode.ShouldBe(status);
        bodies.Count.ShouldBe(expected);
        bodies.ShouldAllBe(body => body == "{\"amount\":123}");
    }

    [Fact]
    public async Task Request_deadline_cancels_retry_delay_without_changing_shared_timeout()
    {
        var calls = 0;
        await using var server = await Server.Start(context =>
        {
            Interlocked.Increment(ref calls);
            context.Response.StatusCode = 503;
            return Task.CompletedTask;
        });
        await using var provider = ResourceProvider(new
        {
            Type = "http.client", BaseAddress = server.Address, Timeout = "00:00:10",
            Retry = new { MaxRetryAttempts = 3, Delay = "00:00:05", UseJitter = false }
        });
        var client = provider.GetRequiredFlowResource<NativeClient>("api");
        await using var component = new HttpClientComponent(new() { Client = client });
        var output = Capture(component.Output);
        await component.Input.SendAsync(Message(new { requestUrl = "/", timeout = "00:00:00.300" }));
        (await output.ReceiveAsync(Deadline)).IsError.ShouldBeTrue();
        Volatile.Read(ref calls).ShouldBe(1);
        client.Timeout.ShouldBe(TimeSpan.FromSeconds(10));
        component.Completion.IsCompleted.ShouldBeFalse();
    }

    [Theory]
    [InlineData("AuthorizationHeader")]
    [InlineData("PostBody")]
    public async Task OAuth_acquires_caches_and_renews_tokens_only_when_sending(string style)
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var tokens = 0;
        var apiHeaders = new List<string>();
        await using var server = await Server.Start(async context =>
        {
            if (context.Request.Path == "/token")
            {
                var form = await context.Request.ReadFormAsync();
                form["grant_type"].ToString().ShouldBe("client_credentials");
                form["scope"].ToString().ShouldBe("orders.write");
                if (style == "PostBody")
                {
                    form["client_id"].ToString().ShouldBe("test-client");
                    form["client_secret"].ToString().ShouldBe("test-secret");
                }
                else context.Request.Headers.Authorization.ToString().ShouldBe("Basic dGVzdC1jbGllbnQ6dGVzdC1zZWNyZXQ=");
                var number = Interlocked.Increment(ref tokens);
                await context.Response.WriteAsJsonAsync(new { access_token = $"token-{number}", token_type = "Bearer", expires_in = 3600 });
                return;
            }
            apiHeaders.Add(context.Request.Headers.Authorization.ToString());
        });
        await using var provider = ResourceProvider(new
        {
            Type = "http.client", BaseAddress = server.Address,
            Authentication = new { Scheme = "OAuthClientCredentials", TokenEndpoint = new Uri(server.Address, "token"),
                ClientId = "test-client", ClientSecret = "test-secret", Scope = "orders.write", ClientCredentialStyle = style }
        }, time);
        await using var app = await provider.BuildApplicationAsync();
        var client = provider.GetRequiredFlowResource<NativeClient>("api");
        Volatile.Read(ref tokens).ShouldBe(0);
        using (await client.GetAsync("/")) { }
        using (await client.GetAsync("/")) { }
        Volatile.Read(ref tokens).ShouldBe(1);
        time.Advance(TimeSpan.FromHours(2));
        using (await client.GetAsync("/")) { }
        Volatile.Read(ref tokens).ShouldBe(2);
        using var manual = new HttpRequestMessage(HttpMethod.Get, "/");
        manual.Headers.Authorization = new("Bearer", "override");
        using (await client.SendAsync(manual)) { }
        Volatile.Read(ref tokens).ShouldBe(2);
        apiHeaders.ShouldBe(["Bearer token-1", "Bearer token-1", "Bearer token-2", "Bearer override"]);
    }

    [Fact]
    public async Task OAuth_failure_does_not_send_an_unauthenticated_request_and_can_recover()
    {
        var attempts = 0;
        var apiCalls = 0;
        await using var server = await Server.Start(async context =>
        {
            if (context.Request.Path == "/token")
            {
                if (Interlocked.Increment(ref attempts) == 1)
                {
                    context.Response.StatusCode = 400;
                    await context.Response.WriteAsJsonAsync(new { error = "invalid_client", error_description = "private token response" });
                }
                else await context.Response.WriteAsJsonAsync(new { access_token = "recovered", token_type = "Bearer", expires_in = 3600 });
            }
            else Interlocked.Increment(ref apiCalls);
        });
        await using var provider = ResourceProvider(new
        {
            Type = "http.client", BaseAddress = server.Address,
            Authentication = new { Scheme = "OAuthClientCredentials", TokenEndpoint = new Uri(server.Address, "token"), ClientId = "test", ClientSecret = "secret" }
        });
        await using var component = new HttpClientComponent(new() { Client = provider.GetRequiredFlowResource<NativeClient>("api") });
        var output = Capture(component.Output);
        await component.Input.SendAsync(Message(new { requestUrl = "/" }));
        var result = await output.ReceiveAsync(Deadline);
        result.IsError.ShouldBeTrue();
        result.Error!.Message.ShouldNotContain("private token response");
        Volatile.Read(ref apiCalls).ShouldBe(0);
        await component.Input.SendAsync(Message(new { requestUrl = "/" }));
        (await output.ReceiveAsync(Deadline)).Value.ShouldBeOfType<HttpResponse>().StatusCode.ShouldBe(200);
        Volatile.Read(ref apiCalls).ShouldBe(1);
    }

    [Fact]
    public async Task OAuth_renewal_is_checked_between_http_retry_attempts()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var tokens = 0;
        var headers = new List<string>();
        await using var server = await Server.Start(async context =>
        {
            if (context.Request.Path == "/token")
            {
                var count = Interlocked.Increment(ref tokens);
                await context.Response.WriteAsJsonAsync(new { access_token = $"token-{count}", token_type = "Bearer", expires_in = 3600 });
                return;
            }
            headers.Add(context.Request.Headers.Authorization.ToString());
            if (headers.Count == 1)
            {
                time.Advance(TimeSpan.FromHours(2));
                context.Response.StatusCode = 503;
            }
        });
        await using var provider = ResourceProvider(new
        {
            Type = "http.client", BaseAddress = server.Address,
            Authentication = new { Scheme = "OAuthClientCredentials", TokenEndpoint = new Uri(server.Address, "token"), ClientId = "test", ClientSecret = "secret" },
            Retry = new { MaxRetryAttempts = 1, Delay = "00:00:00.001", UseJitter = false }
        }, time);
        using var response = await provider.GetRequiredFlowResource<NativeClient>("api").GetAsync("/");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        headers.ShouldBe(["Bearer token-1", "Bearer token-2"]);
    }

    [Fact]
    public async Task OAuth_resources_keep_tokens_separate_and_share_concurrent_acquisition()
    {
        var tokens = 0;
        await using var server = await Server.Start(async context =>
        {
            if (context.Request.Path == "/token")
            {
                var form = await context.Request.ReadFormAsync();
                Interlocked.Increment(ref tokens);
                await Task.Delay(50, context.RequestAborted);
                await context.Response.WriteAsJsonAsync(new { access_token = form["client_id"].ToString(), token_type = "Bearer", expires_in = 3600 });
                return;
            }
            await context.Response.WriteAsync(context.Request.Headers.Authorization.ToString());
        });
        object Resource(string clientId) => new
        {
            Type = "http.client", BaseAddress = server.Address,
            Authentication = new { Scheme = "OAuthClientCredentials", TokenEndpoint = new Uri(server.Address, "token"),
                ClientId = clientId, ClientSecret = "secret", ClientCredentialStyle = "PostBody" }
        };
        var json = JsonSerializer.Serialize(new
        {
            Resources = new { first = Resource("first"), second = Resource("second") },
            Workflows = new { demo = new { send = new { Type = "http.client", Client = "first" } } }
        });
        using var config = (ConfigurationRoot)new ConfigurationBuilder().AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(json))).Build();
        var services = new ServiceCollection().AddHttp();
        services.AddFluxFlowApplication(config);
        await using var provider = services.BuildServiceProvider();
        var first = provider.GetRequiredFlowResource<NativeClient>("first");
        var second = provider.GetRequiredFlowResource<NativeClient>("second");
        var results = await Task.WhenAll(first.GetStringAsync("/"), first.GetStringAsync("/"), second.GetStringAsync("/"));
        results.ShouldBe(["Bearer first", "Bearer first", "Bearer second"]);
        Volatile.Read(ref tokens).ShouldBe(2);
    }

    [Fact]
    public async Task Request_deadline_includes_oauth_token_acquisition()
    {
        var apiCalls = 0;
        await using var server = await Server.Start(async context =>
        {
            if (context.Request.Path != "/token") { Interlocked.Increment(ref apiCalls); return; }
            try { await Task.Delay(Deadline, context.RequestAborted); } catch (OperationCanceledException) { }
        });
        await using var provider = ResourceProvider(new
        {
            Type = "http.client", BaseAddress = server.Address,
            Authentication = new { Scheme = "OAuthClientCredentials", TokenEndpoint = new Uri(server.Address, "token"), ClientId = "test", ClientSecret = "secret" }
        });
        await using var component = new HttpClientComponent(new() { Client = provider.GetRequiredFlowResource<NativeClient>("api") });
        var output = Capture(component.Output);
        await component.Input.SendAsync(Message(new { requestUrl = "/", timeout = "00:00:00.200" }));
        (await output.ReceiveAsync(TimeSpan.FromSeconds(3))).IsError.ShouldBeTrue();
        Volatile.Read(ref apiCalls).ShouldBe(0);
    }
}
