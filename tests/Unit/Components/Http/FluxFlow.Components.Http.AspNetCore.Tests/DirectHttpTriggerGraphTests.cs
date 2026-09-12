using System.Text;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Http.Contracts;
using FluxFlow.Components.RequestReply;
using FluxFlow.Core;
using FluxFlow.Data;
using FluxFlow.Nodes;
using Microsoft.AspNetCore.Http;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Http.AspNetCore.Tests;

public sealed class DirectHttpTriggerGraphTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Direct_trigger_preserves_transport_validation_and_continues_after_rejection(bool invalidFirst)
    {
        var trigger = new HttpTriggerNode(new RequestReplyOptions { Capacity = 1 });
        var requests = trigger.Incoming;
        trigger.Ports.Inputs.ShouldBeEmpty();
        trigger.Ports.Controls.ShouldHaveSingleItem().Key.ShouldBe("Responses");
        trigger.Ports.Controls["Responses"].ShouldBeSameAs(trigger.Responses);
        trigger.Ports.Outputs.ShouldHaveSingleItem().Key.ShouldBe("Output");
        trigger.Ports.Outputs["Output"].ShouldBeSameAs(trigger.Output);
        trigger.Ports.Events.ShouldBeSameAs(trigger.Events);
        await using var app = new FlowApplication(linkCapacity: 1);
        var workflow = app.AddWorkflow("http");
        var source = workflow.Add("trigger", trigger);
        var worker = new ReplyNode();
        var handler = workflow.Add("reply", worker);
        source.Output.LinkTo(handler.Input);
        handler.Output.LinkTo(source.Inputs["Responses"]);
        var rejected = new TaskCompletionSource<FlowEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        var observer = new ActionBlock<FlowMessage>(message =>
        {
            var value = message.Value!.Deserialize<FlowEvent>()!;
            if (value.Name == "http.trigger.reply.rejected") rejected.TrySetResult(value);
        });
        using var observation = trigger.Events.LinkTo(observer, new DataflowLinkOptions { PropagateCompletion = true });
        await app.StartAsync();

        if (invalidFirst)
        {
            using var badResponse = new MemoryStream();
            var badHttp = new DefaultHttpContext();
            badHttp.Request.Method = "GET";
            badHttp.Request.Path = "/invalid";
            badHttp.Response.Body = badResponse;
            var bad = await HttpRequestContext.CreateAsync(badHttp);
            (await requests.SendAsync(bad)).ShouldBeTrue();
            await bad.Completed.WaitAsync(Deadline);
            badHttp.Response.StatusCode.ShouldBe(500);
            var diagnostic = await rejected.Task.WaitAsync(Deadline);
            diagnostic.Level.ShouldBe(FlowEventLevel.Warning);
            diagnostic.Attributes["errorCode"]!.ToString().ShouldBe("http.trigger.reply.invalid_shape");
        }

        using var body = new MemoryStream(new byte[] { 0, 127, 255 });
        using var response = new MemoryStream();
        var http = new DefaultHttpContext();
        http.Request.Method = "POST";
        http.Request.Path = "/echo";
        http.Request.ContentType = "application/octet-stream";
        http.Request.Body = body;
        http.Request.ContentLength = body.Length;
        http.Request.Headers["X-Value"] = "header";
        http.Response.Body = response;
        var context = await HttpRequestContext.CreateAsync(http);
        (await requests.SendAsync(context)).ShouldBeTrue();
        await context.Completed.WaitAsync(Deadline);
        requests.Complete();
        await app.StopAsync().WaitAsync(Deadline);
        await observer.Completion.WaitAsync(Deadline);

        worker.LastRequest.ShouldNotBeNull();
        worker.LastRequest.Method.ShouldBe("POST");
        worker.LastRequest.Path.ShouldBe("/echo");
        worker.LastRequest.Headers["X-Value"].ShouldBe(["header"]);
        worker.LastRequest.Body.ShouldBe(new byte[] { 0, 127, 255 });
        worker.LastRequest.ContentType.ShouldBe("application/octet-stream");
        http.Response.StatusCode.ShouldBe(201);
        http.Response.ContentType.ShouldBe("text/plain; charset=utf-8");
        Encoding.UTF8.GetString(response.ToArray()).ShouldBe("accepted");
        trigger.ProductionCompletion.IsCompletedSuccessfully.ShouldBeTrue();
        trigger.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        app.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        if (!invalidFirst) rejected.Task.IsCompleted.ShouldBeFalse();
    }

    private sealed class ReplyNode : FlowNode
    {
        public HttpTriggerRequest? LastRequest { get; private set; }
        protected override async Task ProcessAsync(FlowMessage message)
        {
            LastRequest = message.Value!.Deserialize<HttpTriggerRequest>()!;
            var response = LastRequest.Path == "/invalid"
                ? FlowValue.From(42)
                : FlowValue.From(HttpTriggerReply.Text("accepted", statusCode: 201));
            await EmitAsync(message.With(response), Stopping);
        }
    }
}
