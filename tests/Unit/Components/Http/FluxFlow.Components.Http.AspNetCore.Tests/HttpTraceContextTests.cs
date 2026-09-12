using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Http.AspNetCore;
using FluxFlow.Components.Http.Contracts;
using FluxFlow.Components.RequestReply;
using FluxFlow.Data;
using FluxFlow.Nodes;
using Microsoft.AspNetCore.Http;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Http.AspNetCore.Tests;

[CollectionDefinition("HTTP trace context", DisableParallelization = true)]
public sealed class HttpTraceContextCollection;

[Collection("HTTP trace context")]
public sealed class HttpTraceContextTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Queued_contexts_preserve_each_request_trace(bool fireAndForget, bool traced)
    {
        var deadline = TimeSpan.FromSeconds(30);
        var activities = new ConcurrentQueue<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "FluxFlow.Core",
            Sample = (ref ActivityCreationOptions<ActivityContext> options) =>
                options.Parent.TraceFlags.HasFlag(ActivityTraceFlags.Recorded)
                    ? ActivitySamplingResult.AllDataAndRecorded : ActivitySamplingResult.AllData,
            ActivityStarted = activities.Enqueue
        };
        ActivitySource.AddActivityListener(listener);
        using var startup = new Activity("startup").SetIdFormat(ActivityIdFormat.W3C).Start();
        await using var bridge = new RequestReplyCoordinator<HttpTriggerRequest, HttpTriggerReply>(new()
        {
            Capacity = 8, Timeout = deadline,
            Mode = fireAndForget ? RequestReplyMode.FireAndForget : RequestReplyMode.RequestReply
        });
        var output = new BufferBlock<FlowMessage>(new() { BoundedCapacity = 8 });
        using var link = bridge.Output.LinkTo(output);
        var contexts = new List<(HttpRequestContext Context, ActivityContext Parent)>();
        for (var i = 0; i < 3; i++)
        {
            HttpRequestContext context;
            ActivityContext parent;
            using (FlowTracing.SuppressContext())
            {
                using var request = traced ? new Activity("server").SetIdFormat(ActivityIdFormat.W3C)
                    .SetParentId(ActivityTraceId.CreateRandom(), ActivitySpanId.CreateRandom(),
                        i == 1 ? ActivityTraceFlags.None : ActivityTraceFlags.Recorded).Start() : null;
                if (request is not null) request.TraceStateString = $"tenant=t{i}";
                parent = request?.Context ?? default;
                var http = new DefaultHttpContext();
                http.Request.Method = "GET";
                http.Request.Path = $"/request/{i}";
                http.Response.Body = new MemoryStream();
                context = await HttpRequestContext.CreateAsync(http);
            }
            contexts.Add((context, parent));
            // Queue under unrelated context after the original server activity stopped.
            (await bridge.Incoming.SendAsync(context).WaitAsync(deadline)).ShouldBeTrue();
        }
        await bridge.StartAsync();
        var traces = new List<ActivityTraceId>();
        foreach (var expected in contexts)
        {
            var message = await output.ReceiveAsync(deadline);
            message.Value!.Deserialize<HttpTriggerRequest>()!.Path.ShouldBe(expected.Context.Request.Path);
            message.Headers.TryGetValue("traceparent", out var parent);
            message.Headers.TryGetValue("tracestate", out var state);
            var hasContext = ActivityContext.TryParse(parent, state, out var observed);
            if (traced || !fireAndForget)
            {
                hasContext.ShouldBeTrue();
                observed.TraceId.ShouldNotBe(startup.TraceId);
                if (traced) observed.TraceId.ShouldBe(expected.Parent.TraceId);
                observed.TraceState.ShouldBe(expected.Parent.TraceState);
                observed.TraceFlags.ShouldBe(expected.Parent.TraceFlags);
                traces.Add(observed.TraceId);
                if (fireAndForget) observed.SpanId.ShouldBe(expected.Parent.SpanId);
                else
                {
                    var exchange = activities.Single(activity => activity.SpanId == observed.SpanId);
                    exchange.OperationName.ShouldBe("flow.request");
                    exchange.ParentSpanId.ShouldBe(expected.Parent.SpanId);
                }
            }
            else
            {
                hasContext.ShouldBeFalse();
                message.Headers.ShouldBeEmpty();
            }
            message.CorrelationId.ShouldNotBeNull();
            if (!fireAndForget)
                (await bridge.Responses.SendAsync(message.With(FlowValue.From(HttpTriggerReply.Text("ok")))).WaitAsync(deadline)).ShouldBeTrue();
            await expected.Context.Completed.WaitAsync(deadline);
        }
        traces.Distinct().Count().ShouldBe(traces.Count);
        Activity.Current.ShouldBeSameAs(startup);
        bridge.StopProduction();
        await bridge.ProductionCompletion.WaitAsync(deadline);
    }
}
