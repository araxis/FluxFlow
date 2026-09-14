using System.Text;
using System.Diagnostics;
using System.Text.Json;
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

[Collection("HTTP trace context")]
public sealed class DirectHttpTriggerGraphTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Direct_trigger_preserves_transport_validation_and_continues_after_rejection(bool invalidFirst, bool tracing)
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => tracing && source.Name == FlowDiagnostics.ListenerName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);
        using var diagnostics = new DiagnosticObserver();
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
            var value = message.Value!.Json.Deserialize<FlowEvent>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
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
            var report = diagnostics.Values.ShouldHaveSingleItem();
            report.Exception.ShouldBeOfType<JsonException>().StackTrace.ShouldNotBeNullOrWhiteSpace();
            report.MessageId.ShouldBe(worker.LastResponse!.MessageId);
            report.CorrelationId.ShouldBe(worker.LastResponse.CorrelationId);
            report.TraceId.ShouldBe(worker.LastResponse.TraceId);
            report.ComponentType.ShouldBe(typeof(HttpTriggerNode).FullName);
            (report.Activity is not null).ShouldBe(tracing);
            if (tracing)
            {
                report.Activity!.Status.ShouldBe(ActivityStatusCode.Error);
                ActivityContext.TryParse(worker.LastResponse.Headers["traceparent"], null, out var parent).ShouldBeTrue();
                report.Activity.TraceId.ShouldBe(parent.TraceId);
                report.Activity.ParentSpanId.ShouldBe(parent.SpanId);
            }
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
        diagnostics.Values.Count.ShouldBe(invalidFirst ? 1 : 0);
    }

    private sealed class ReplyNode : FlowNode
    {
        public HttpTriggerRequest? LastRequest { get; private set; }
        public FlowMessage? LastResponse { get; private set; }
        protected override async Task ProcessAsync(FlowMessage message)
        {
            LastRequest = message.Value!.Json.Deserialize<HttpTriggerRequest>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            var response = LastRequest.Path == "/invalid"
                ? ((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(42, new JsonSerializerOptions(JsonSerializerDefaults.Web))))
                : ((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(HttpTriggerReply.Text("accepted", statusCode: 201), new JsonSerializerOptions(JsonSerializerDefaults.Web))));
            LastResponse = message.With(response);
            await EmitAsync(LastResponse, Stopping);
        }
    }

    private sealed class DiagnosticObserver : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>, IDisposable
    {
        public List<FlowProcessingException> Values { get; } = [];
        private readonly List<IDisposable> _subscriptions = [];
        private readonly IDisposable _discovery;
        public DiagnosticObserver() => _discovery = DiagnosticListener.AllListeners.Subscribe(this);
        public void OnNext(DiagnosticListener value)
        {
            if (value.Name == FlowDiagnostics.ListenerName)
                _subscriptions.Add(value.Subscribe(this, name => name == FlowDiagnostics.ProcessingExceptionEvent));
        }
        public void OnNext(KeyValuePair<string, object?> value)
        {
            Values.Add((FlowProcessingException)value.Value!);
            throw new InvalidOperationException("observer failed");
        }
        public void OnCompleted() { }
        public void OnError(Exception error) { }
        public void Dispose()
        {
            _discovery.Dispose();
            foreach (var subscription in _subscriptions) subscription.Dispose();
        }
    }
}
