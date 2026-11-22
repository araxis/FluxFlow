using System.Threading.Tasks.Dataflow;
using FluxFlow.Application;
using Microsoft.AspNetCore.Http;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Http.Tests;

public sealed partial class HttpTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Incomplete_post_response_is_an_error_without_replaying_the_post(bool stall)
    {
        var attempts = 0;
        var recovery = 0;
        var partial = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = await Server.Start(async context =>
        {
            if (context.Request.Method != "POST") { context.Response.StatusCode = 405; return; }
            if (context.Request.Path == "/next")
            {
                Interlocked.Increment(ref recovery);
                await context.Response.WriteAsync("recovered");
                return;
            }
            Interlocked.Increment(ref attempts);
            context.Response.ContentLength = 100;
            await context.Response.WriteAsync("partial");
            await context.Response.Body.FlushAsync();
            partial.TrySetResult();
            if (stall)
            {
                try { await Task.Delay(Deadline, context.RequestAborted); }
                catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
            }
            else context.Abort();
        });
        await using var provider = ResourceProvider(new { Type = "http.client", BaseAddress = server.Address, Retry = new { MaxRetryAttempts = 0 } });
        await using var component = new HttpClientComponent(new() { Client = provider.GetRequiredFlowResource<System.Net.Http.HttpClient>("api") });
        var output = Capture(component.Output);
        var post = Message(new { method = "POST", requestUrl = "/submit", body = "first", timeout = stall ? "00:00:01" : "00:00:30" });
        await component.Input.SendAsync(post);
        await partial.Task.WaitAsync(Deadline);
        var failure = await output.ReceiveAsync(Deadline);
        failure.IsError.ShouldBeTrue();
        failure.CausationId.ShouldBe(post.MessageId);

        var next = Message(new { method = "POST", requestUrl = "/next", body = "second" });
        await component.Input.SendAsync(next);
        var response = await output.ReceiveAsync(Deadline);
        response.CausationId.ShouldBe(next.MessageId);
        response.Value.ShouldBeOfType<HttpResponse>().Body.ShouldBe("recovered");
        Volatile.Read(ref attempts).ShouldBe(1);
        Volatile.Read(ref recovery).ShouldBe(1);
    }
}
