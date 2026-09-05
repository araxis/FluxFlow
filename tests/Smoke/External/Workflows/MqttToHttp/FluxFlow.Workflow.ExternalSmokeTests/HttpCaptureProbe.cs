using System.Net;
using System.Threading.Channels;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace FluxFlow.Workflow.ExternalSmokeTests;

internal sealed class HttpCaptureProbe : IAsyncDisposable
{
    private readonly WebApplication _application;
    private readonly Channel<CapturedHttpRequest> _requests;

    private HttpCaptureProbe(
        WebApplication application,
        Channel<CapturedHttpRequest> requests,
        Uri captureUrl)
    {
        _application = application;
        _requests = requests;
        CaptureUrl = captureUrl;
    }

    internal Uri CaptureUrl { get; }

    internal static async ValueTask<HttpCaptureProbe> StartAsync(
        CancellationToken cancellationToken = default)
    {
        var requests = Channel.CreateUnbounded<CapturedHttpRequest>(
            new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false
            });
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        var application = builder.Build();
        application.MapPost("/capture", async context =>
        {
            using var reader = new StreamReader(context.Request.Body);
            var body = await reader.ReadToEndAsync(context.RequestAborted);
            await requests.Writer.WriteAsync(
                new CapturedHttpRequest(
                    context.Request.Headers["x-flow-scenario"].ToString(),
                    context.Request.Headers["x-mqtt-topic"].ToString(),
                    body),
                context.RequestAborted);
            context.Response.StatusCode = StatusCodes.Status202Accepted;
            await context.Response.WriteAsync("{\"accepted\":true}", context.RequestAborted);
        });
        await application.StartAsync(cancellationToken);

        var addresses = application.Services
            .GetRequiredService<IServer>()
            .Features
            .Get<IServerAddressesFeature>()?
            .Addresses
            ?? throw new InvalidOperationException("The HTTP capture server did not publish an address.");
        var address = addresses.ShouldHaveSingleItem();
        return new HttpCaptureProbe(application, requests, new Uri(new Uri(address), "/capture"));
    }

    internal async ValueTask<CapturedHttpRequest> ReceiveAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        return await _requests.Reader.ReadAsync(timeoutSource.Token);
    }

    public async ValueTask DisposeAsync()
    {
        _requests.Writer.TryComplete();
        await _application.StopAsync();
        await _application.DisposeAsync();
    }
}

internal sealed record CapturedHttpRequest(string Scenario, string Topic, string Body);
