using FluxFlow.Components.Http.AspNetCore;
using FluxFlow.Components.Http.Contracts;
using FluxFlow.Components.RequestReply;
using FluxFlow.Nodes;
using Microsoft.AspNetCore.Http;
using Shouldly;
using System.Text;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Components.Http.AspNetCore.Tests;

public sealed class HttpRequestContextTests
{
    [Fact]
    public async Task CreateAsync_MapsTheHttpContext()
    {
        var http = NewContext("POST", "/hook", "?a=1", "hi", "text/plain");
        http.Request.Headers["X-Test"] = "v";

        var context = await HttpRequestContext.CreateAsync(http);

        context.Request.Method.ShouldBe("POST");
        context.Request.Path.ShouldBe("/hook");
        context.Request.QueryString.ShouldBe("?a=1");
        context.Request.ContentType.ShouldBe("text/plain");
        context.Request.Headers["X-Test"][0].ShouldBe("v");
        Encoding.UTF8.GetString(context.Request.Body!).ShouldBe("hi");
    }

    [Fact]
    public async Task CreateAsync_ReadsCorrelationHeaderWhenConfigured()
    {
        var http = NewContext("GET", "/x");
        http.Request.Headers["X-Correlation-Id"] = "trace-42";

        var context = await HttpRequestContext.CreateAsync(http, correlationHeader: "X-Correlation-Id");

        context.CorrelationId.ShouldBe(new CorrelationId("trace-42"));
    }

    [Fact]
    public async Task ReplyAsync_WritesStatusBodyAndContentType()
    {
        var http = NewContext("GET", "/x");
        var context = await HttpRequestContext.CreateAsync(http);

        await context.ReplyAsync(HttpTriggerReply.Text("ok", statusCode: 201));

        http.Response.StatusCode.ShouldBe(201);
        http.Response.ContentType.ShouldBe("text/plain; charset=utf-8");
        ReadResponse(http).ShouldBe("ok");
        context.Completed.IsCompletedSuccessfully.ShouldBeTrue();
    }

    [Fact]
    public async Task FailAsync_TimeoutWrites504_AndCompletes()
    {
        var http = NewContext("GET", "/x");
        var context = await HttpRequestContext.CreateAsync(http);

        await context.FailAsync(new TimeoutException());

        http.Response.StatusCode.ShouldBe(StatusCodes.Status504GatewayTimeout);
        context.Completed.IsCompletedSuccessfully.ShouldBeTrue();
    }

    [Fact]
    public async Task EndToEnd_BridgePlusHandler_WritesResponseToHttpContext()
    {
        await using var bridge = new RequestReplyBridge<HttpTriggerRequest, HttpTriggerReply>();
        // The "graph": echo the request body back, preserving the correlation id.
        var handler = new ActionBlock<FlowMessage<HttpTriggerRequest>>(request =>
            bridge.Responses.Post(request.With(
                HttpTriggerReply.Text($"echo:{Encoding.UTF8.GetString(request.Payload.Body ?? [])}"))));
        bridge.Output.LinkTo(handler);

        var http = NewContext("POST", "/hook", body: "hi", contentType: "text/plain");
        var context = await HttpRequestContext.CreateAsync(http);

        await bridge.Incoming.SendAsync(context);
        await context.Completed.WaitAsync(TimeSpan.FromSeconds(30));

        http.Response.StatusCode.ShouldBe(200);
        ReadResponse(http).ShouldBe("echo:hi");
    }

    private static DefaultHttpContext NewContext(
        string method,
        string path,
        string? query = null,
        string? body = null,
        string? contentType = null)
    {
        var http = new DefaultHttpContext();
        http.Request.Method = method;
        http.Request.Path = path;
        if (query is not null)
        {
            http.Request.QueryString = new QueryString(query);
        }

        if (contentType is not null)
        {
            http.Request.ContentType = contentType;
        }

        if (body is not null)
        {
            var bytes = Encoding.UTF8.GetBytes(body);
            http.Request.Body = new MemoryStream(bytes);
            http.Request.ContentLength = bytes.Length;
        }

        http.Response.Body = new MemoryStream();
        return http;
    }

    private static string ReadResponse(HttpContext http)
    {
        http.Response.Body.Position = 0;
        using var reader = new StreamReader(http.Response.Body, Encoding.UTF8, leaveOpen: true);
        return reader.ReadToEnd();
    }
}
