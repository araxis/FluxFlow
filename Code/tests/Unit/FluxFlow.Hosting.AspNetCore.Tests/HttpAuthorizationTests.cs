using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Application;
using FluxFlow.Application.Addressing;
using FluxFlow.Components.Http;
using FluxFlow.Configuration.Application;
using FluxFlow.Configuration.Components.Http;
using FluxFlow.Configuration.Components.Mapping;
using FluxFlow.Core.Nodes;
using FluxFlow.Expressions.Jsonata;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace FluxFlow.Hosting.AspNetCore.Tests;

public sealed class HttpAuthorizationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Host_policy_rejects_unauthorized_requests_before_the_workflow_and_allows_correlated_replies(bool upload)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.Configuration.AddJsonFile(Path.Combine(AppContext.BaseDirectory, "HttpSample.json"));
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddAuthentication("Test")
            .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>("Test", _ => { });
        builder.Services.AddAuthorization(options => options.AddPolicy("Write", policy =>
            policy.RequireAuthenticatedUser().RequireClaim("permission", "write")));
        builder.Services.AddHttpTriggers().AddHttp().AddMapper().AddJsonataExpressionEngine()
            .AddFluxFlowApplication(builder.Configuration.GetRequiredSection("FluxFlow")).AddFluxFlow();

        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        var endpointCalls = 0;
        app.MapGroup("/protected").MapFluxFlowHttpTriggers().RequireAuthorization("Write")
            .AddEndpointFilter(async (context, next) =>
            {
                Interlocked.Increment(ref endpointCalls);
                return await next(context);
            });
        await app.StartAsync();
        var workflow = upload ? "uploads" : "api";
        var trigger = (HttpTrigger)app.Services.GetWorkflowServices(workflow).GetRequiredKeyedService<IFlowNode>($"{workflow}.receive");
        using var output = app.Services.ObserveOutput(PortAddress.Parse($"{workflow}.receive.Output"));
        using var mapped = app.Services.ObserveOutput(PortAddress.Parse($"{workflow}.reply.Output"));
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()), Timeout = TimeSpan.FromSeconds(10) };
            foreach (var (identity, status) in new (string?, HttpStatusCode)[]
            {
                (null, HttpStatusCode.Unauthorized),
                ("invalid", HttpStatusCode.Unauthorized),
                ("reader", HttpStatusCode.Forbidden)
            })
            {
                using var request = CreateRequest(upload, 0, identity);
                using var response = await client.SendAsync(request);
                response.StatusCode.ShouldBe(status);
                Volatile.Read(ref endpointCalls).ShouldBe(0);
                trigger.PendingCount.ShouldBe(0);
                output.Messages.TryReceive(out _).ShouldBeFalse();
                mapped.Messages.TryReceive(out _).ShouldBeFalse();
            }

            var replies = await Task.WhenAll(Enumerable.Range(1, 4).Select(async index =>
            {
                using var request = CreateRequest(upload, index, "writer");
                using var response = await client.SendAsync(request);
                response.StatusCode.ShouldBe(HttpStatusCode.OK);
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                var body = json.RootElement;
                if (upload)
                {
                    body.GetProperty("fields").GetProperty("device")[0].GetString().ShouldBe($"sensor-{index}");
                    body.GetProperty("files")[0].GetProperty("fileName").GetString().ShouldBe($"sensor-{index}.bin");
                    body.GetProperty("files")[0].GetProperty("length").GetInt32().ShouldBe(3);
                }
                else
                {
                    body.GetProperty("deviceId").GetString().ShouldBe($"sensor-{index}");
                    body.GetProperty("reading").GetInt32().ShouldBe(index);
                }
                return body.GetProperty("messageId").GetString();
            }));
            replies.ShouldAllBe(id => !string.IsNullOrWhiteSpace(id));
            replies.Distinct().Count().ShouldBe(4);
            Volatile.Read(ref endpointCalls).ShouldBe(4);
            trigger.PendingCount.ShouldBe(0);
        }
        finally
        {
            await app.StopAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }
        trigger.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        app.Services.GetRequiredKeyedService<FlowWorkflow>(workflow).Completion.IsCompletedSuccessfully.ShouldBeTrue();
    }

    private static HttpRequestMessage CreateRequest(bool upload, int index, string? identity)
    {
        HttpContent content;
        if (upload)
        {
            var form = new MultipartFormDataContent();
            form.Add(new StringContent($"sensor-{index}"), "device");
            form.Add(new ByteArrayContent([0, 255, (byte)index]), "upload", $"sensor-{index}.bin");
            content = form;
        }
        else content = new StringContent($$"""{"value":{{index}},"unit":"C"}""", Encoding.UTF8, "application/json");
        var request = new HttpRequestMessage(HttpMethod.Post,
            upload ? "/protected/uploads" : $"/protected/devices/sensor-{index}/readings") { Content = content };
        if (identity is not null) request.Headers.Authorization = new("Test", identity);
        return request;
    }

    // Test identities only; authentication challenges and policy enforcement remain native middleware behavior.
    private sealed class TestAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var header = Request.Headers.Authorization.ToString();
            if (header.Length == 0) return Task.FromResult(AuthenticateResult.NoResult());
            if (header is not ("Test reader" or "Test writer"))
                return Task.FromResult(AuthenticateResult.Fail("Invalid test identity."));
            var permission = header == "Test writer" ? "write" : "read";
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, header), new Claim("permission", permission)], Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}
